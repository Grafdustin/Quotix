using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Quotix.Messages;
using Quotix.Services;
using Wpf.Ui.Controls;
using Quotix.ViewModels;

namespace Quotix;

/// <summary>
/// 主窗口，负责导航、主题切换、设置面板和内嵌对话框功能。
/// </summary>
public partial class MainWindow : FluentWindow
{
    /// <summary>
    /// 获取当前数据上下文作为 MainViewModel。
    /// </summary>
    private MainViewModel VM => (MainViewModel)DataContext;
    private readonly AppSettingsService _settingsService;

    /// <summary>
    /// 初始化 MainWindow 实例。
    /// </summary>
    /// <param name="viewModel">主视图模型</param>
    /// <param name="settingsService">应用设置服务</param>
    public MainWindow(MainViewModel viewModel, AppSettingsService settingsService)
    {
        DataContext = viewModel;
        _settingsService = settingsService;
        InitializeComponent();
        LoadIcon();
        LoadTitleBarIcon();
        Loaded += OnLoaded;
        Closed += OnClosed;

        // 订阅更新可用消息
        WeakReferenceMessenger.Default.Register<UpdateAvailableMessage>(this, (r, m) =>
        {
            Dispatcher.Invoke(() => SetUpdateBadge(m.Value));
        });
    }

    /// <summary>
    /// 窗口关闭时调用。
    /// </summary>
    private void OnClosed(object? sender, EventArgs e)
    {
    }

    /// <summary>
    /// 窗口加载时调用，初始化导航状态和主题。
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 恢复导航栏折叠状态
        RootNavView.IsPaneOpen = !_settingsService.NavigationCollapsed;

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
    }

    /// <summary>
    /// 设置更新徽章显示状态
    /// </summary>
    private void SetUpdateBadge(bool show)
    {
        UpdateRedDot.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// NavigationViewItem 点击事件（Click 代替 SelectionChanged，避开 TargetPageType 约束）。
    /// </summary>
    private void OnNavItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is NavigationViewItem nvi)
        {
            var tag = nvi.Tag?.ToString();
            if (tag == null) return;

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
    /// <param name="items">导航项列表</param>
    /// <param name="tag">目标 Tag</param>
    private static void SetNavItemActive(System.Collections.IList items, string tag)
    {
        foreach (var item in items)
        {
            if (item is Wpf.Ui.Controls.NavigationViewItem nvi)
            {
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
    /// 切换导航面板展开/收起状态。
    /// </summary>
    private void TogglePane_Click(object sender, RoutedEventArgs e)
    {
        RootNavView.IsPaneOpen = !RootNavView.IsPaneOpen;

        // 持久化导航栏状态
        _settingsService.NavigationCollapsed = !RootNavView.IsPaneOpen;

        UpdatePaneToggleIcon();
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

    // ─── 内嵌密码弹窗 ───

    private DispatcherFrame? _pwdFrame;
    private string? _pwdResult;
    private bool _pwdRevealed;

    /// <summary>
    /// 取消按钮点击事件（内嵌密码弹窗）。
    /// </summary>
    private void PasswordOverlay_CancelClick(object sender, RoutedEventArgs e)
    {
        _pwdResult = null;
        PasswordOverlay.Visibility = Visibility.Collapsed;
        _pwdFrame!.Continue = false;
    }

    /// <summary>
    /// 确认按钮点击事件（内嵌密码弹窗）。
    /// </summary>
    private void PasswordOverlay_ConfirmClick(object sender, RoutedEventArgs e)
    {
        _pwdResult = _pwdRevealed ? PwdRevealBox.Text : PwdBox.Password;
        PasswordOverlay.Visibility = Visibility.Collapsed;
        _pwdFrame!.Continue = false;
    }

    /// <summary>
    /// 密码框按键事件，回车触发确认。
    /// </summary>
    private void PwdBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            PasswordOverlay_ConfirmClick(sender, e);
    }

    /// <summary>
    /// 密码显示/隐藏切换按钮点击事件。
    /// </summary>
    private void PwdToggle_Click(object sender, RoutedEventArgs e)
    {
        _pwdRevealed = !_pwdRevealed;

        if (_pwdRevealed)
        {
            // 切换到明文：先显示 TextBox，再隐藏 PasswordBox（避免中间空白帧）
            PwdRevealBox.Text = PwdBox.Password;
            PwdRevealBox.Visibility = Visibility.Visible;
            PwdBox.Visibility = Visibility.Collapsed;
            PwdToggleIcon.Symbol = SymbolRegular.EyeOff16;
            PwdRevealBox.Focus();
            PwdRevealBox.SelectAll();
        }
        else
        {
            // 切换回密码：先显示 PasswordBox，再隐藏 TextBox
            PwdBox.Password = PwdRevealBox.Text;
            PwdBox.Visibility = Visibility.Visible;
            PwdRevealBox.Visibility = Visibility.Collapsed;
            PwdToggleIcon.Symbol = SymbolRegular.Eye16;
            PwdBox.Focus();
        }
    }

    /// <summary>
    /// 程序内嵌密码输入弹窗 — 半透明遮罩 + 居中卡片 + PasswordBox。
    /// 返回用户输入的密码，取消时返回 null。
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">消息内容</param>
    /// <param name="errorMessage">错误提示（为 null 时不显示）</param>
    /// <returns>用户输入的密码，取消时返回 null</returns>
    public string? ShowInlinePasswordPrompt(
        string title,
        string message,
        string? errorMessage = null)
    {
        Dispatcher.VerifyAccess();

        // 填充内容
        PwdDialogTitle.Text = title;
        PwdDialogMessage.Text = message;

        // 错误提示
        if (!string.IsNullOrEmpty(errorMessage))
        {
            PwdErrorText.Text = errorMessage;
            PwdErrorText.Visibility = Visibility.Visible;
        }
        else
        {
            PwdErrorText.Visibility = Visibility.Collapsed;
        }

        // 清空密码框，重置显示模式
        PwdBox.Password = string.Empty;
        PwdRevealBox.Text = string.Empty;
        _pwdRevealed = false;
        PwdBox.Visibility = Visibility.Visible;
        PwdRevealBox.Visibility = Visibility.Collapsed;
        PwdToggleIcon.Symbol = SymbolRegular.Eye16;

        // 确保在最前面
        Panel.SetZIndex(PasswordOverlay, 1002);

        // 显示
        PasswordOverlay.Visibility = Visibility.Visible;
        PasswordOverlay.UpdateLayout();

        // 自动聚焦到密码框
        PwdBox.Focus();

        // 阻塞等待用户操作
        _pwdResult = null;
        _pwdFrame = new DispatcherFrame();
        Dispatcher.PushFrame(_pwdFrame);

        return _pwdResult;
    }
}
