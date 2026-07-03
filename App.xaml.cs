using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Quotix.Services;
using Quotix.Views;

namespace Quotix;

/// <summary>
/// 应用程序入口类。
/// 负责单实例检测、DI 容器构建、数据库初始化和主窗口启动。
/// </summary>
public partial class App : Application
{
    /// <summary>单实例互斥体名称</summary>
    private const string AppMutexName = "Quotix_SingleInstance_Mutex";

    /// <summary>单实例互斥体</summary>
    private static Mutex? _singleInstanceMutex;

    /// <summary>DI 容器</summary>
    private ServiceProvider? _serviceProvider;

    /// <summary>恢复窗口所需的 user32 API</summary>
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    /// <summary>应用程序启动入口</summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        // ── 单实例检测（已运行则激活已有窗口）────
        _singleInstanceMutex = new Mutex(true, AppMutexName, out bool createdNew);
        if (!createdNew)
        {
            var current = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(current.ProcessName))
            {
                if (p.Id != current.Id)
                {
                    IntPtr hWnd = p.MainWindowHandle;
                    if (hWnd != IntPtr.Zero && IsWindow(hWnd))
                    {
                        ShowWindow(hWnd, SW_RESTORE);
                        SetForegroundWindow(hWnd);
                    }
                    break;
                }
            }

            _singleInstanceMutex.Dispose();
            Shutdown();
            return;
        }

        try
        {
            await StartApplicationAsync();
        }
        catch (Exception ex)
        {
            // 最外层兜底：确保任何启动阶段的异常都被记录并反馈给用户
            LogException(ex);
            try
            {
                System.Windows.MessageBox.Show(
                    $"Quotix 启动失败：\n\n{GetInnerMostMessage(ex)}\n\n详细信息已写入：\n{AppPaths.ErrorLogPath}",
                    "Quotix 启动错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            catch { }
            Shutdown();
        }
    }

    /// <summary>执行应用程序主启动流程</summary>
    private async Task StartApplicationAsync()
    {
        // 构建 DI 容器
        _serviceProvider = DiConfig.Build();

        // 从设置恢复主题
        var isDark = LoadThemeSetting();
        ApplicationThemeManager.Apply(
            isDark ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.None
        );

        // 捕获未处理异常
        DispatcherUnhandledException += (s, args) =>
        {
            LogException(args.Exception);
            var innerMsg = GetInnerMostMessage(args.Exception);
            var msg = $"程序发生未处理异常：\n\n{innerMsg}\n\n详细信息已写入：\n{AppPaths.ErrorLogPath}";

            try
            {
                _serviceProvider?.GetService<DialogService>()?.ShowError(msg, "Quotix 错误");
            }
            catch
            {
                System.Windows.MessageBox.Show(msg, "Quotix 错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                LogException(ex);
        };

        // 启动闪屏
        var splash = new SplashWindow();
        splash.Show();

        // 并行：数据库初始化 + 闪屏动画
        var dbInitTask = InitializeDatabaseAsync();
        _ = MonitorDatabaseInit(dbInitTask, splash);

        // 等待闪屏动画完成（最长 15 秒超时保护，防止动画卡死导致永远不显示主窗口）
        var readyTask = splash.WaitForReadyAsync();
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15));
        var completedTask = await Task.WhenAny(readyTask, timeoutTask);

        if (completedTask != readyTask)
        {
            // 超时：强制快进闪屏
            splash.FastForward();
            await readyTask;
        }

        // 确保数据库初始化完成
        await dbInitTask;

        // 数据库迁移
        _serviceProvider.GetRequiredService<Services.MigrationService>().Run();

        // 预热缓存（利用闪屏时间段完成）
        var cacheTask = Task.Run(() => _serviceProvider.GetRequiredService<Services.CacheService>().WarmUp());

        // 进入主窗口
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
        await splash.FadeOutAsync();

        // 显式指定 MainWindow（SplashWindow 关闭后 WPF 可能不会自动提升）
        Application.Current.MainWindow = mainWindow;

        // 主窗口关闭时退出应用
        mainWindow.Closed += (_, _) => Shutdown();
    }

    /// <summary>应用程序退出时释放资源</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>监控数据库初始化任务，完成后通知闪屏</summary>
    private static async Task MonitorDatabaseInit(Task dbInitTask, SplashWindow splash)
    {
        await dbInitTask;
        splash.SignalExternalReady();
        splash.FastForward();
    }

    /// <summary>模拟数据库初始化（后续可替换为实际加载逻辑）</summary>
    private static async Task InitializeDatabaseAsync()
    {
        await Task.Delay(600);
    }

    /// <summary>获取异常的最内层消息</summary>
    private static string GetInnerMostMessage(Exception ex)
    {
        var msg = ex.Message;
        var inner = ex.InnerException;
        while (inner != null)
        {
            msg = inner.Message;
            inner = inner.InnerException;
        }
        return msg;
    }

    /// <summary>从设置文件加载主题偏好</summary>
    private static bool LoadThemeSetting()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsPath))
            {
                var json = File.ReadAllText(AppPaths.SettingsPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
                return settings?.DarkMode ?? false;
            }
        }
        catch { }
        return false;
    }

    /// <summary>将异常信息写入错误日志文件</summary>
    private static void LogException(Exception ex)
    {
        try
        {
            var logPath = AppPaths.ErrorLogPath;
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new System.Text.StringBuilder();
            var current = ex;
            int level = 0;
            while (current != null)
            {
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Level {level}] {current.GetType().FullName}: {current.Message}");
                sb.AppendLine(current.StackTrace ?? "(no stack trace)");
                sb.AppendLine();
                current = current.InnerException;
                level++;
            }

            File.AppendAllText(logPath, sb.ToString());
        }
        catch { }
    }
}
