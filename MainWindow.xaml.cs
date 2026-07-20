using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading.Tasks;
using Quotix.Services;
using Wpf.Ui.Controls;
using Quotix.ViewModels;
using Quotix.Models;
using CommunityToolkit.Mvvm.Messaging;
using Quotix.Messages;

namespace Quotix;

/// <summary>
/// 主窗口，负责导航、主题切换、设置面板、更新弹窗和内嵌对话框功能。
/// </summary>
public partial class MainWindow : FluentWindow
{
    /// <summary>
    /// 获取当前数据上下文作为 MainViewModel。
    /// </summary>
    private MainViewModel VM => (MainViewModel)DataContext;
    private readonly AppSettingsService _settingsService;
    private readonly UpdatePipeline _updatePipeline;

    /// <summary>
    /// 更新弹窗是否处于后台下载模式（弹窗已关闭但下载仍在继续）
    /// </summary>
    private bool _isBackgroundDownloading;

    /// <summary>
    /// 初始化 MainWindow 实例。
    /// </summary>
    public MainWindow(MainViewModel viewModel, AppSettingsService settingsService, UpdatePipeline updatePipeline)
    {
        DataContext = viewModel;
        _settingsService = settingsService;
        _updatePipeline = updatePipeline;
        InitializeComponent();
        SettingsCard.SizeChanged += (_, _) => UpdateSettingsCardClip();
        LoadIcon();
        LoadTitleBarIcon();
        Loaded += OnLoaded;
        Closed += OnClosed;

        // 更新弹窗绑定到 Pipeline 的 State 对象
        UpdateOverlay.DataContext = _updatePipeline.State;

        // 订阅 State 属性变化 → 更新箭头按钮进度
        _updatePipeline.State.PropertyChanged += OnUpdateStateChanged;

        // 订阅显示更新弹窗消息
        WeakReferenceMessenger.Default.Register<ShowUpdateOverlayMessage>(this, (r, m) =>
        {
            m.Reply(true);
            Dispatcher.Invoke(() => ShowUpdateOverlay());
        });
    }

    /// <summary>
    /// 窗口关闭时调用。
    /// </summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        _updatePipeline.State.PropertyChanged -= OnUpdateStateChanged;
        _updatePipeline.Dispose();
    }

    /// <summary>
    /// 将设置弹窗内容裁剪为圆角矩形，使顶部/底部圆角真正可见（避免子内容溢出露出遮罩直角）。
    /// </summary>
    private void UpdateSettingsCardClip()
    {
        if (SettingsContentGrid == null) return;
        const double radius = 18.0;
        SettingsContentGrid.Clip = new RectangleGeometry(
            new Rect(0, 0, SettingsContentGrid.ActualWidth, SettingsContentGrid.ActualHeight), radius, radius);
    }

    /// <summary>
    /// 更新 State 属性变化处理：驱动箭头按钮进度显示。
    /// </summary>
    private void OnUpdateStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdateState.Stage) ||
            e.PropertyName == nameof(UpdateState.ProgressInt))
        {
            Dispatcher.Invoke(UpdateArrowButton);
        }
    }

    /// <summary>
    /// 更新按钮显示内容：空闲时显示"更新"，下载中显示进度百分比。
    /// </summary>
    private void UpdateArrowButton()
    {
        if (UpdateNavItem.Content is not StackPanel sp)
        {
            // 首次调用：将 Content 从简单 TextBlock 替换为 StackPanel
            UpdateNavItem.Content = null;
            sp = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _arrowText = new System.Windows.Controls.TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.Normal,
                Foreground = Brushes.Red,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            sp.Children.Add(_arrowText);
            UpdateNavItem.Content = sp;
        }

        var stage = _updatePipeline.State.Stage;
        if (stage == UpdateStage.Downloading)
        {
            // 下载中：显示进度百分比
            _arrowText!.Text = $"{_updatePipeline.State.ProgressInt}%";
            UpdateNavItem.Visibility = Visibility.Visible;
        }
        else if (stage == UpdateStage.ReadyToInstall)
        {
            // 下载完成：显示"安装"
            _arrowText!.Text = "安装";
            UpdateNavItem.Visibility = Visibility.Visible;
        }
        else if (stage == UpdateStage.UpdateAvailable)
        {
            // 有新版本：显示"更新"
            _arrowText!.Text = "更新";
            UpdateNavItem.Visibility = Visibility.Visible;
        }
        else
        {
            // 其他状态：如果没在后台下载就隐藏
            if (!_isBackgroundDownloading)
            {
                UpdateNavItem.Visibility = Visibility.Collapsed;
            }
        }
    }
    private System.Windows.Controls.TextBlock? _arrowText;

    /// <summary>
    /// 窗口加载时调用，初始化导航状态、主题和异步检查更新。
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 恢复导航栏折叠状态
        RootNavView.IsPaneOpen = !_settingsService.NavigationCollapsed;

        // 折叠时隐藏主题切换按钮（48px 窄栏仅容纳收缩按钮，保持图标居中对齐）
        UpdatePaneHeaderVisibility();

        // 订阅主题变化
        VM.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsDarkMode))
                UpdateThemeIcon();
        };

        // 初始化：默认激活新建报价
        SyncNavSelection("new-quotation");
        UpdateThemeIcon();
        UpdatePaneToggleIcon();

        // 给设置弹窗注入 SettingsViewModel（它不在 ContentControl 的 DataTemplate 体系中）
        SettingsViewControl.DataContext = VM.SettingsViewModel;

        // 异步检查更新（不阻塞 UI 加载）
        _ = AutoCheckUpdateAsync();
    }

    // ══════════════════════════════════════
    //  更新弹窗
    // ══════════════════════════════════════

    /// <summary>
    /// 启动时自动检查更新：异步检测，有更新则弹窗提醒。
    /// </summary>
    private async Task AutoCheckUpdateAsync()
    {
        try
        {
            var updateInfo = await _updatePipeline.CheckAsync();

            if (updateInfo != null)
            {
                // 有新版本：显示更新导航项 + 弹窗提醒
                Dispatcher.Invoke(() =>
                {
                    UpdateArrowButton();
                    ShowUpdateOverlay();
                });
            }
        }
        catch
        {
            // 网络错误等，静默处理
        }
    }

    /// <summary>
    /// 显示更新弹窗。
    /// 如果安装包已下载，直接跳到 ReadyToInstall 状态。
    /// </summary>
    private void ShowUpdateOverlay()
    {
        _isBackgroundDownloading = false;

        // 如果已下载安装包，直接设为 ReadyToInstall
        if (_updatePipeline.IsInstallerDownloaded &&
            _updatePipeline.State.Stage != UpdateStage.Downloading)
        {
            _updatePipeline.State.Stage = UpdateStage.ReadyToInstall;
            _updatePipeline.State.Message = "下载完成，点击安装即可更新";
        }

        UpdateOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 点击遮罩层：将下载放入后台（关闭弹窗但继续下载）
    /// </summary>
    private void UpdateOverlay_BackgroundClick(object sender, MouseButtonEventArgs e)
    {
        if (_updatePipeline.State.Stage == UpdateStage.Downloading)
        {
            // 下载中点击遮罩：放入后台
            _isBackgroundDownloading = true;
            UpdateOverlay.Visibility = Visibility.Collapsed;
            UpdateArrowButton();
        }
    }

    /// <summary>
    /// 点击弹窗卡片：阻止事件冒泡到遮罩层。
    /// </summary>
    private void UpdateOverlay_CardClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    /// <summary>
    /// 更新导航项点击事件 — 弹出更新通知卡片。
    /// </summary>
    private void OnUpdateItemClick(object sender, RoutedEventArgs e)
    {
        // 如果正在后台下载，先停止后台状态
        _isBackgroundDownloading = false;
        ShowUpdateOverlay();
    }

    /// <summary>
    /// 关闭更新弹窗。
    /// </summary>
    private void UpdateOverlay_Close(object sender, RoutedEventArgs e)
    {
        UpdateOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 更新弹窗左边按钮点击 — 取消下载 / 稍后。
    /// </summary>
    private void UpdateOverlay_LeftButtonClick(object sender, RoutedEventArgs e)
    {
        if (_updatePipeline.State.Stage == UpdateStage.Downloading)
        {
            // 下载中：取消下载
            _updatePipeline.CancelDownload();
        }
        else
        {
            // 其他状态：关闭弹窗
            UpdateOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 更新弹窗主按钮点击 — 根据 Stage 执行对应操作。
    /// </summary>
    private async void UpdateOverlay_PrimaryClick(object sender, RoutedEventArgs e)
    {
        switch (_updatePipeline.State.Stage)
        {
            case UpdateStage.UpdateAvailable:
                // 开始下载
                UpdateNavItem.Visibility = Visibility.Visible;
                await _updatePipeline.DownloadAsync();

                // 下载完成后，弹窗仍然打开，用户可点击「安装更新」
                break;

            case UpdateStage.Failed:
                // 如果安装包已经存在，失败后的“重试”应优先重试安装，而不是重新下载。
                if (_updatePipeline.IsInstallerDownloaded)
                {
                    _updatePipeline.Install();
                }
                else
                {
                    UpdateNavItem.Visibility = Visibility.Visible;
                    await _updatePipeline.DownloadAsync();
                }
                break;

            case UpdateStage.Downloading:
                // 下载中点击主按钮：放入后台
                _isBackgroundDownloading = true;
                UpdateOverlay.Visibility = Visibility.Collapsed;
                UpdateArrowButton();
                break;

            case UpdateStage.ReadyToInstall:
                // 安装更新（会关闭应用）
                _updatePipeline.Install();
                break;
        }
    }

    // ══════════════════════════════════════
    //  导航与设置
    // ══════════════════════════════════════

    /// <summary>
    /// NavigationViewItem 点击事件（Click 代替 SelectionChanged，避开 TargetPageType 约束）。
    /// </summary>
    private void OnNavItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is NavigationViewItem nvi)
        {
            var tag = nvi.Tag?.ToString();
            if (tag == null) return;

            // 更新项有自己独立的处理，不在这里处理
            if (tag == "update") return;

            // 切换内容
            switch (tag)
            {
                case "new-quotation":
                    VM.OpenNewQuotationTab();
                    break;
                case "history":
                    VM.OpenHistoryTab();
                    break;
                case "product-db":
                    VM.OpenProductDatabaseTab();
                    break;
                case "header-db":
                    VM.OpenHeaderDatabaseTab();
                    break;
                case "settings":
                    // 设置面板居中覆盖显示，不切换 Tab
                    SettingsOverlay.Visibility = Visibility.Visible;
                    UpdateSettingsCardClip();
                    return; // 不更新导航栏激活状态，保持当前页面的激活状态
            }

            // 更新激活状态
            SyncNavSelection(tag);
        }
    }

    /// <summary>
    /// 关闭设置覆盖层（仅 X 按钮触发）。
    /// </summary>
    private void SettingsOverlay_Close(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 同步导航栏的激活状态。
    /// </summary>
    /// <param name="tag">目标导航项的 Tag</param>
    private void SyncNavSelection(string tag)
    {
        SetNavItemActive(RootNavView.MenuItems, tag);
        SetNavItemActive(RootNavView.FooterMenuItems, tag);
    }

    /// <summary>
    /// 设置导航项的活动状态。
    /// </summary>
    private static void SetNavItemActive(System.Collections.IList items, string tag)
    {
        foreach (var item in items)
        {
            if (item is Wpf.Ui.Controls.NavigationViewItem nvi)
            {
                // 更新项不参与导航激活状态
                if (nvi.Tag?.ToString() == "update")
                {
                    nvi.IsActive = false;
                    continue;
                }
                nvi.IsActive = nvi.Tag?.ToString() == tag;
            }
        }
    }

    /// <summary>
    /// 更新主题图标（根据当前是深色还是浅色模式）。
    /// </summary>
    private void UpdateThemeIcon()
    {
        ThemeIcon.Symbol = VM.IsDarkMode
            ? SymbolRegular.WeatherSunny20
            : SymbolRegular.WeatherMoon20;
    }

    /// <summary>
    /// 更新面板折叠/展开图标。
    /// </summary>
    private void UpdatePaneToggleIcon()
    {
        // 面板展开时显示左箭头（点击后向左收起）
        // 面板收起时显示右箭头（点击后向右展开）
        PaneToggleIcon.Symbol = RootNavView.IsPaneOpen
            ? SymbolRegular.PanelLeft20
            : SymbolRegular.PanelRight20;
    }

    /// <summary>
    /// 根据面板展开/折叠状态调整 PaneHeader 布局：折叠（窄栏 48px）时隐藏主题切换按钮，
    /// 仅保留收缩按钮，使其图标与下方导航项图标居中对齐。
    /// </summary>
    private void UpdatePaneHeaderVisibility()
    {
        ThemeToggleButton.Visibility = RootNavView.IsPaneOpen
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    /// <summary>
    /// 切换导航面板展开/收起状态。
    /// </summary>
    private void TogglePane_Click(object sender, RoutedEventArgs e)
    {
        RootNavView.IsPaneOpen = !RootNavView.IsPaneOpen;

        // 持久化导航栏状态
        _settingsService.NavigationCollapsed = !RootNavView.IsPaneOpen;

        UpdatePaneToggleIcon();
        // 折叠时隐藏主题按钮，保持收缩按钮与导航项图标居中对齐
        UpdatePaneHeaderVisibility();
    }

    /// <summary>
    /// 切换主题（深色/浅色）按钮点击事件。
    /// </summary>
    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        VM.ToggleDarkModeCommand.Execute(null);
    }

    /// <summary>
    /// 加载窗口图标。
    /// </summary>
    private void LoadIcon()
    {
        try
        {
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                using var stream = new System.IO.FileStream(iconPath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Icon = decoder.Frames[0];
            }
        }
        catch { }
    }

    /// <summary>
    /// 加载标题栏图标。
    /// </summary>
    private void LoadTitleBarIcon()
    {
        try
        {
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                using var stream = new System.IO.FileStream(iconPath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

                var bestFrame = decoder.Frames[0];
                foreach (var frame in decoder.Frames)
                {
                    if (frame.PixelWidth > bestFrame.PixelWidth)
                        bestFrame = frame;
                }

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bestFrame));
                var ms = new System.IO.MemoryStream();
                encoder.Save(ms);
                ms.Position = 0;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();

                TitleBarIconImage.Source = bmp;
            }
        }
        catch { }
    }

    // ─── 内嵌弹窗 ───

    private DispatcherFrame? _dialogFrame;

    /// <summary>
    /// 取消按钮点击事件（内嵌对话框）。
    /// </summary>
    private void DialogOverlay_CancelClick(object sender, RoutedEventArgs e)
    {
        _dialogResult = false;
        DialogOverlay.Visibility = Visibility.Collapsed;
        _dialogFrame!.Continue = false;
    }

    /// <summary>
    /// 确认按钮点击事件（内嵌对话框）。
    /// </summary>
    private void DialogOverlay_PrimaryClick(object sender, RoutedEventArgs e)
    {
        _dialogResult = true;
        DialogOverlay.Visibility = Visibility.Collapsed;
        _dialogFrame!.Continue = false;
    }

    private bool _dialogResult;

    /// <summary>
    /// 程序内嵌弹窗 — 半透明遮罩 + 居中卡片，完全避开 OS 窗口尺寸问题。
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">消息内容</param>
    /// <param name="icon">图标</param>
    /// <param name="primaryText">确认按钮文本</param>
    /// <param name="cancelText">取消按钮文本（为 null 时不显示）</param>
    /// <returns>用户是否确认</returns>
    public bool ShowInlineDialog(
        string title,
        string message,
        SymbolRegular icon,
        string primaryText,
        string? cancelText = null)
    {
        Dispatcher.VerifyAccess();

        // 填充内容
        DialogTitle.Text = title;
        DialogMessage.Text = message;
        DialogIcon.Symbol = icon;

        DialogPrimaryBtn.Content = primaryText;

        if (cancelText != null)
        {
            DialogCancelBtn.Content = cancelText;
            DialogCancelBtn.Visibility = Visibility.Visible;
        }
        else
        {
            DialogCancelBtn.Visibility = Visibility.Collapsed;
        }

        // 确保在最前面
        Panel.SetZIndex(DialogOverlay, 1001);

        // 显示
        DialogOverlay.Visibility = Visibility.Visible;

        // 让 WPF 完成一轮 layout，确保卡片内容已 arrange
        DialogOverlay.UpdateLayout();

        // 阻塞等待用户操作
        _dialogResult = false;
        _dialogFrame = new DispatcherFrame();
        Dispatcher.PushFrame(_dialogFrame);

        return _dialogResult;
    }

}
