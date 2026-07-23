using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
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
    private sealed record OnboardingStep(
        string Target,
        string? PageTag,
        string Title,
        string Body,
        string Hint);

    /// <summary>
    /// 获取当前数据上下文作为 MainViewModel。
    /// </summary>
    private MainViewModel VM => (MainViewModel)DataContext;
    private readonly AppSettingsService _settingsService;
    private readonly UpdatePipeline _updatePipeline;
    private readonly List<OnboardingStep> _onboardingSteps = new()
    {
        new("dashboard", "dashboard", "首页",
            "查看客户、产品、报价和金额趋势。",
            ""),
        new("product-db", "product-db", "产品列表",
            "导入 NDT/RVI 价表和货期表。",
            ""),
        new("header-db", "header-db", "收录信息",
            "录入负责人、客户和报价说明。",
            ""),
        new("new-quotation", "new-quotation", "新建报价",
            "填写客户信息和产品明细。",
            ""),
        new("quick-code", "new-quotation", "快捷输入",
            "在编号框输入关键字，选择产品。",
            "提示:请先在设置菜单的产品列表中导入excel，再去设置的快捷输入中编辑映射列"),
        new("history", "history", "报价历史",
            "搜索、编辑或重新导出报价。",
            ""),
        new("settings", null, "设置",
            "调整导出、快捷输入、外观、更新和反馈。",
            ""),
        new("", "dashboard", "结束",
            "在设置中导入选择的产品列表的Excel文件、按照提示内容开始吧。",
            "")
    };
    private int _onboardingIndex;
    private readonly DispatcherTimer _updateCheckTimer = new();
    private bool _isCheckingUpdate;
    private string _lastPromptedUpdateVersion = "";

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
        _updateCheckTimer.Interval = TimeSpan.FromMinutes(30);
        _updateCheckTimer.Tick += UpdateCheckTimer_Tick;

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
        WeakReferenceMessenger.Default.Register<EditQuotationMessage>(this, (r, m) =>
            Dispatcher.Invoke(() => SyncNavSelection("new-quotation")));
        WeakReferenceMessenger.Default.Register<OpenTabMessage>(this, (r, m) =>
            Dispatcher.Invoke(() => SyncNavSelection(m.Value)));
        WeakReferenceMessenger.Default.Register<ShowOnboardingGuideMessage>(this, (r, m) =>
        {
            m.Reply(true);
            Dispatcher.Invoke(() => ShowOnboardingGuide(markAsSeen: false));
        });
    }

    /// <summary>
    /// 窗口关闭时调用。
    /// </summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        _updateCheckTimer.Stop();
        _updateCheckTimer.Tick -= UpdateCheckTimer_Tick;
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

        // 初始化：默认激活首页
        SyncNavSelection("dashboard");
        UpdateThemeIcon();
        UpdatePaneToggleIcon();

        // 给设置弹窗注入 SettingsViewModel（它不在 ContentControl 的 DataTemplate 体系中）
        SettingsViewControl.DataContext = VM.SettingsViewModel;

        // 异步检查更新（不阻塞 UI 加载），并在程序运行中持续检查新发布版本。
        _ = AutoCheckUpdateAsync();
        _updateCheckTimer.Start();

        if (!_settingsService.HasSeenOnboarding)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(() => ShowOnboardingGuide(markAsSeen: false)));
        }
    }

    /// <summary>显示首次使用引导。</summary>
    private void ShowOnboardingGuide(bool markAsSeen)
    {
        _onboardingIndex = 0;
        TutorialOverlay.Visibility = Visibility.Visible;
        if (markAsSeen)
            _settingsService.HasSeenOnboarding = true;
        ShowOnboardingStep();
    }

    /// <summary>刷新当前引导步骤的文字、页面和高亮位置。</summary>
    private void ShowOnboardingStep()
    {
        if (_onboardingIndex < 0)
            _onboardingIndex = 0;
        if (_onboardingIndex >= _onboardingSteps.Count)
        {
            FinishOnboarding();
            return;
        }

        var step = _onboardingSteps[_onboardingIndex];
        NavigateForOnboarding(step);

        TutorialStepText.Text = $"{_onboardingIndex + 1} / {_onboardingSteps.Count}";
        TutorialTitleText.Text = step.Title;
        TutorialBodyText.Text = step.Body;
        TutorialHintText.Text = step.Hint;
        TutorialHintText.Visibility = string.IsNullOrWhiteSpace(step.Hint)
            ? Visibility.Collapsed
            : Visibility.Visible;
        TutorialProgressBar.Value = (_onboardingIndex + 1) * 100.0 / _onboardingSteps.Count;
        TutorialPrevButton.IsEnabled = _onboardingIndex > 0;
        TutorialNextButton.Content = _onboardingIndex == _onboardingSteps.Count - 1 ? "完成" : "下一步";

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => UpdateOnboardingHighlight(step)));
    }

    /// <summary>根据引导步骤切换到对应页面。</summary>
    private void NavigateForOnboarding(OnboardingStep step)
    {
        if (step.Target == "settings")
        {
            SettingsOverlay.Visibility = Visibility.Visible;
            UpdateSettingsCardClip();
            return;
        }

        SettingsOverlay.Visibility = Visibility.Collapsed;

        switch (step.PageTag)
        {
            case "dashboard":
                VM.OpenDashboardTab();
                SyncNavSelection("dashboard");
                break;
            case "product-db":
                VM.OpenProductDatabaseTab();
                SyncNavSelection("product-db");
                break;
            case "header-db":
                VM.OpenHeaderDatabaseTab();
                SyncNavSelection("header-db");
                break;
            case "new-quotation":
                VM.OpenNewQuotationTab();
                SyncNavSelection("new-quotation");
                break;
            case "history":
                VM.OpenHistoryTab();
                SyncNavSelection("history");
                break;
        }
    }

    /// <summary>更新引导高亮框位置。</summary>
    private void UpdateOnboardingHighlight(OnboardingStep step)
    {
        var target = GetOnboardingTarget(step.Target);
        if (target == null || !target.IsVisible || target.ActualWidth <= 0 || target.ActualHeight <= 0)
        {
            TutorialHighlight.Visibility = Visibility.Collapsed;
            CenterOnboardingCard();
            return;
        }

        target.BringIntoView();
        MainContentHost.UpdateLayout();
        TutorialOverlay.UpdateLayout();

        TutorialHighlight.Visibility = Visibility.Visible;
        var rootPoint = target.TransformToAncestor(RootLayout).Transform(new Point(0, 0));
        var overlayPoint = TutorialOverlay.TransformToAncestor(RootLayout).Transform(new Point(0, 0));
        var x = Math.Max(8, rootPoint.X - overlayPoint.X - 6);
        var y = Math.Max(8, rootPoint.Y - overlayPoint.Y - 6);
        var width = Math.Min(TutorialOverlay.ActualWidth - x - 8, target.ActualWidth + 12);
        var height = Math.Min(TutorialOverlay.ActualHeight - y - 8, target.ActualHeight + 12);

        if (target is NavigationViewItem)
        {
            var navPoint = RootNavView.TransformToAncestor(RootLayout).Transform(new Point(0, 0));
            var navOverlayPoint = TutorialOverlay.TransformToAncestor(RootLayout).Transform(new Point(0, 0));
            var navWidth = RootNavView.ActualWidth > 0
                ? RootNavView.ActualWidth
                : RootNavView.OpenPaneLength;

            x = Math.Max(6, navPoint.X - navOverlayPoint.X + 6);
            y += 8;
            width = Math.Max(48, navWidth - 14);
            height = 32;
        }

        Canvas.SetLeft(TutorialHighlight, x);
        Canvas.SetTop(TutorialHighlight, y);
        TutorialHighlight.Width = Math.Max(40, width);
        TutorialHighlight.Height = Math.Max(36, height);

        PositionOnboardingCard(x, y, width, height);
    }

    /// <summary>把引导说明卡放到高亮区域附近，并避免超出视窗。</summary>
    private void PositionOnboardingCard(double x, double y, double width, double height)
    {
        TutorialCard.UpdateLayout();

        const double gap = 16;
        const double edge = 18;
        var overlayWidth = Math.Max(1, TutorialOverlay.ActualWidth);
        var overlayHeight = Math.Max(1, TutorialOverlay.ActualHeight);
        var cardWidth = TutorialCard.ActualWidth > 0 ? TutorialCard.ActualWidth : TutorialCard.Width;
        var cardHeight = TutorialCard.ActualHeight > 0 ? TutorialCard.ActualHeight : 230;

        var left = x + width + gap;
        var top = y;

        if (left + cardWidth > overlayWidth - edge)
            left = x - cardWidth - gap;

        if (left < edge)
        {
            left = Math.Min(overlayWidth - cardWidth - edge, Math.Max(edge, x + width / 2 - cardWidth / 2));
            top = y + height + gap;
        }

        if (top + cardHeight > overlayHeight - edge)
            top = y - cardHeight - gap;
        if (top < edge)
            top = edge;

        TutorialCard.HorizontalAlignment = HorizontalAlignment.Left;
        TutorialCard.VerticalAlignment = VerticalAlignment.Top;
        TutorialCard.Margin = new Thickness(left, top, 0, 0);
    }

    /// <summary>目标不可用时把引导说明卡放回底部居中。</summary>
    private void CenterOnboardingCard()
    {
        TutorialCard.HorizontalAlignment = HorizontalAlignment.Center;
        TutorialCard.VerticalAlignment = VerticalAlignment.Bottom;
        TutorialCard.Margin = new Thickness(0, 0, 0, 34);
    }

    /// <summary>获取当前引导步骤需要高亮的界面元素。</summary>
    private FrameworkElement? GetOnboardingTarget(string target) => target switch
    {
        "content" => MainContentHost,
        "dashboard" => DashboardNavItem,
        "product-db" => ProductDbNavItem,
        "header-db" => HeaderDbNavItem,
        "new-quotation" => NewQuotationNavItem,
        "quick-code" => FindVisualByAutomationId(MainContentHost, "QuotationCodeInput"),
        "history" => HistoryNavItem,
        "settings" => SettingsCard,
        _ => null
    };

    /// <summary>按 AutomationId 在视觉树里查找元素，用于定位模板内部控件。</summary>
    private static FrameworkElement? FindVisualByAutomationId(DependencyObject root, string automationId)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element
                && AutomationProperties.GetAutomationId(element) == automationId)
                return element;

            var nested = FindVisualByAutomationId(child, automationId);
            if (nested != null)
                return nested;
        }

        return null;
    }

    /// <summary>完成或跳过引导。</summary>
    private void FinishOnboarding()
    {
        TutorialOverlay.Visibility = Visibility.Collapsed;
        TutorialHighlight.Visibility = Visibility.Collapsed;
        _settingsService.HasSeenOnboarding = true;
    }

    // ══════════════════════════════════════
    //  更新弹窗
    // ══════════════════════════════════════

    /// <summary>
    /// 启动时自动检查更新：异步检测，有更新则弹窗提醒。
    /// </summary>
    private async Task AutoCheckUpdateAsync()
    {
        await CheckAndPromptUpdateAsync();
    }

    private async void UpdateCheckTimer_Tick(object? sender, EventArgs e)
    {
        await CheckAndPromptUpdateAsync();
    }

    /// <summary>
    /// 检查更新并在发现新版本时弹出提醒；同一版本只主动弹出一次。
    /// </summary>
    private async Task CheckAndPromptUpdateAsync()
    {
        if (_isCheckingUpdate)
            return;

        if (UpdateOverlay.Visibility == Visibility.Visible)
            return;

        if (_updatePipeline.State.Stage is UpdateStage.Downloading or UpdateStage.Installing)
            return;

        _isCheckingUpdate = true;
        try
        {
            var updateInfo = await _updatePipeline.CheckAsync();

            Dispatcher.Invoke(UpdateArrowButton);

            if (updateInfo == null)
                return;

            if (string.Equals(_lastPromptedUpdateVersion, updateInfo.Version, StringComparison.OrdinalIgnoreCase))
                return;

            _lastPromptedUpdateVersion = updateInfo.Version;
            Dispatcher.Invoke(ShowUpdateOverlay);
        }
        catch
        {
            // 网络错误等，静默处理
        }
        finally
        {
            _isCheckingUpdate = false;
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
                case "dashboard":
                    VM.OpenDashboardTab();
                    break;
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

    /// <summary>引导上一步。</summary>
    private void TutorialPrev_Click(object sender, RoutedEventArgs e)
    {
        _onboardingIndex--;
        ShowOnboardingStep();
    }

    /// <summary>引导下一步或完成。</summary>
    private void TutorialNext_Click(object sender, RoutedEventArgs e)
    {
        _onboardingIndex++;
        ShowOnboardingStep();
    }

    /// <summary>跳过引导。</summary>
    private void TutorialSkip_Click(object sender, RoutedEventArgs e)
    {
        FinishOnboarding();
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
            ? SymbolRegular.PanelLeftContract20
            : SymbolRegular.PanelLeftExpand20;
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

        HeaderButtonsPanel.Margin = RootNavView.IsPaneOpen
            ? new Thickness(-2, 4, 0, 0)
            : new Thickness(-2, 4, 0, 0);
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
    private Func<string, string?>? _dialogInputValidator;

    /// <summary>
    /// 取消按钮点击事件（内嵌对话框）。
    /// </summary>
    private void DialogOverlay_CancelClick(object sender, RoutedEventArgs e)
    {
        _dialogResult = false;
        _dialogInputResult = null;
        DialogOverlay.Visibility = Visibility.Collapsed;
        _dialogFrame!.Continue = false;
    }

    /// <summary>
    /// 确认按钮点击事件（内嵌对话框）。
    /// </summary>
    private void DialogOverlay_PrimaryClick(object sender, RoutedEventArgs e)
    {
        if (DialogInputBox.Visibility == Visibility.Visible && _dialogInputValidator != null)
        {
            var error = _dialogInputValidator(DialogInputBox.Text);
            if (!string.IsNullOrWhiteSpace(error))
            {
                DialogInputErrorText.Text = error;
                DialogInputErrorText.Visibility = Visibility.Visible;
                DialogInputBox.Focus();
                return;
            }
        }

        _dialogResult = true;
        _dialogInputResult = DialogInputBox.Visibility == Visibility.Visible
            ? DialogInputBox.Text
            : null;
        DialogOverlay.Visibility = Visibility.Collapsed;
        _dialogFrame!.Continue = false;
    }

    private bool _dialogResult;
    private string? _dialogInputResult;

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
        DialogInputBox.Visibility = Visibility.Collapsed;
        DialogInputBox.Text = "";
        DialogInputErrorText.Visibility = Visibility.Collapsed;
        DialogInputErrorText.Text = "";
        _dialogInputValidator = null;

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

    /// <summary>
    /// 程序内嵌输入弹窗，返回用户确认后的文本；取消时返回 null。
    /// </summary>
    public string? ShowInlineInputDialog(
        string title,
        string message,
        string initialValue,
        SymbolRegular icon,
        string primaryText,
        string cancelText,
        Func<string, string?>? validator = null)
    {
        Dispatcher.VerifyAccess();

        DialogTitle.Text = title;
        DialogMessage.Text = message;
        DialogIcon.Symbol = icon;
        DialogInputBox.Text = initialValue;
        DialogInputBox.Visibility = Visibility.Visible;
        DialogInputErrorText.Visibility = Visibility.Collapsed;
        DialogInputErrorText.Text = "";
        _dialogInputValidator = validator;
        DialogPrimaryBtn.Content = primaryText;
        DialogCancelBtn.Content = cancelText;
        DialogCancelBtn.Visibility = Visibility.Visible;

        Panel.SetZIndex(DialogOverlay, 1001);
        DialogOverlay.Visibility = Visibility.Visible;
        DialogOverlay.UpdateLayout();
        DialogInputBox.Focus();
        DialogInputBox.SelectAll();

        _dialogResult = false;
        _dialogInputResult = null;
        _dialogFrame = new DispatcherFrame();
        Dispatcher.PushFrame(_dialogFrame);

        return _dialogResult ? _dialogInputResult : null;
    }

}
