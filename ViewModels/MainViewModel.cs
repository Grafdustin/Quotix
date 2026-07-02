using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Quotix.Messages;
using Quotix.Services;
using Quotix.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Quotix.ViewModels;

/// <summary>
/// 主窗口 ViewModel。
/// 负责标签页管理、主题切换、全局进度显示和消息通信。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly AppSettingsService _appSettings;
    private readonly ThemeService _themeService;
    private readonly DialogService _dialog;
    private readonly NewQuotationViewModel _newQuotationVM;
    private readonly SettingsViewModel _settingsVM;

    /// <summary>设置页 ViewModel（供绑定使用）</summary>
    public SettingsViewModel SettingsViewModel => _settingsVM;

    /// <summary>标签页集合</summary>
    public ObservableCollection<TabItemViewModel> Tabs { get; } = new();

    /// <summary>当前活动标签页</summary>
    [ObservableProperty]
    private TabItemViewModel? _activeTab;

    /// <summary>当前活动标签页的内容（供绑定使用）</summary>
    public object? ActiveTabContent => ActiveTab?.View;

    partial void OnActiveTabChanged(TabItemViewModel? value)
    {
        OnPropertyChanged(nameof(ActiveTabContent));
    }

    /// <summary>状态栏文本</summary>
    [ObservableProperty] private string _statusText = "就绪";

    /// <summary>进度条是否可见</summary>
    [ObservableProperty] private bool _isProgressVisible;

    /// <summary>进度百分比（0~100）</summary>
    [ObservableProperty] private double _progressPercentage;

    /// <summary>进度提示文本</summary>
    [ObservableProperty] private string _progressText = "请稍候...";

    /// <summary>是否深色模式（与主题服务同步）</summary>
    [ObservableProperty] private bool _isDarkMode;

    /// <summary>是否有可用更新（控制更新徽章显示）</summary>
    [ObservableProperty] private bool _hasUpdate;

    /// <summary>预注入的可复用 ViewModel（每次开标签页复用同一实例）</summary>
    private readonly ProductDatabaseViewModel _productDbVM;
    private readonly HeaderDatabaseViewModel _headerDbVM;
    private readonly HistoryViewModel _historyVM;

    public MainViewModel(
        AppSettingsService appSettings,
        ThemeService themeService,
        DialogService dialog,
        NewQuotationViewModel newQuotationVM,
        SettingsViewModel settingsVM,
        ProductDatabaseViewModel productDbVM,
        HeaderDatabaseViewModel headerDbVM,
        HistoryViewModel historyVM)
    {
        _appSettings = appSettings;
        _themeService = themeService;
        _dialog = dialog;
        _newQuotationVM = newQuotationVM;
        _settingsVM = settingsVM;
        _productDbVM = productDbVM;
        _headerDbVM = headerDbVM;
        _historyVM = historyVM;

        // 订阅 Messenger 消息（替代事件耦合）
        WeakReferenceMessenger.Default.Register<ThemeChangedMessage>(this, (r, m) =>
        {
            _themeService.Apply(m.Value);
            IsDarkMode = m.Value;
            _settingsVM.IsDarkMode = m.Value;
        });
        WeakReferenceMessenger.Default.Register<AboutRequestedMessage>(this, (r, m) =>
        {
            m.Reply(true);
            ShowAboutDialog();
        });
        WeakReferenceMessenger.Default.Register<EditQuotationMessage>(this, (r, m) =>
            EditQuotation(m.Value));
        WeakReferenceMessenger.Default.Register<ProgressMessage>(this, (r, m) =>
        {
            var s = m.Value;
            IsProgressVisible = s.IsVisible;
            ProgressPercentage = s.Percentage;
            ProgressText = s.Text;
        });
        WeakReferenceMessenger.Default.Register<UpdateAvailableMessage>(this, (r, m) =>
        {
            HasUpdate = m.Value;
        });

        // 加载主题设置
        _themeService.Load();
        IsDarkMode = _themeService.IsDarkMode;
        _settingsVM.IsDarkMode = _themeService.IsDarkMode;

        // 默认打开新建报价标签页
        OpenNewQuotationTab();
    }

    // ============ 标签页管理 ============

    // ============ 标签页管理 ============

    /// <summary>打开新建报价标签页（已存在则激活）</summary>
    public void OpenNewQuotationTab()
    {
        var existing = Tabs.FirstOrDefault(t => t.TabId == "new-quotation");
        if (existing != null)
        {
            ActivateTab(existing);
            return;
        }

        var view = new NewQuotationView { DataContext = _newQuotationVM };
        var tab = new TabItemViewModel
        {
            TabId = "new-quotation",
            Title = "新建报价",
            Content = _newQuotationVM,
            View = view,
            CanClose = true
        };
        Tabs.Add(tab);
        ActivateTab(tab);
    }

    /// <summary>打开报价历史标签页（已存在则激活）</summary>
    public void OpenHistoryTab()
    {
        var existing = Tabs.FirstOrDefault(t => t.TabId == "history");
        if (existing != null)
        {
            ActivateTab(existing);
            return;
        }

        var view = new HistoryView { DataContext = _historyVM };
        var tab = new TabItemViewModel
        {
            TabId = "history",
            Title = "报价历史",
            Content = _historyVM,
            View = view,
            CanClose = true
        };
        Tabs.Add(tab);
        ActivateTab(tab);
    }

    /// <summary>打开产品列表标签页（已存在则激活，并刷新数据）</summary>
    public void OpenProductDatabaseTab()
    {
        var existing = Tabs.FirstOrDefault(t => t.TabId == "product-db");
        if (existing != null)
        {
            ActivateTab(existing);
            return;
        }

        var vm = _productDbVM;
        var view = new ProductDatabaseView { DataContext = vm };
        var tab = new TabItemViewModel
        {
            TabId = "product-db",
            Title = "产品列表",
            Content = vm,
            View = view,
            CanClose = true
        };
        Tabs.Add(tab);
        ActivateTab(tab);
        vm.Refresh();
    }

    /// <summary>打开收录信息标签页（已存在则激活，并刷新数据）</summary>
    public void OpenHeaderDatabaseTab()
    {
        var existing = Tabs.FirstOrDefault(t => t.TabId == "header-db");
        if (existing != null)
        {
            ActivateTab(existing);
            return;
        }

        var vm = _headerDbVM;
        var view = new HeaderDatabaseView { DataContext = vm };
        var tab = new TabItemViewModel
        {
            TabId = "header-db",
            Title = "收录信息",
            Content = vm,
            View = view,
            CanClose = true
        };
        Tabs.Add(tab);
        ActivateTab(tab);
        vm.Refresh();
    }

    /// <summary>打开设置标签页（已存在则激活）</summary>
    public void OpenSettingsTab()
    {
        var existing = Tabs.FirstOrDefault(t => t.TabId == "settings");
        if (existing != null)
        {
            ActivateTab(existing);
            return;
        }

        var view = new SettingsView { DataContext = _settingsVM };
        var tab = new TabItemViewModel
        {
            TabId = "settings",
            Title = "设置",
            Content = _settingsVM,
            View = view,
            CanClose = true
        };
        _settingsVM.IsDarkMode = IsDarkMode;
        Tabs.Add(tab);
        ActivateTab(tab);
    }

    /// <summary>激活指定标签页</summary>
    public void ActivateTab(TabItemViewModel tab)
    {
        foreach (var t in Tabs)
            t.IsSelected = false;
        tab.IsSelected = true;
        ActiveTab = tab;
    }

    /// <summary>关闭指定标签页，并自动切换到相邻标签页</summary>
    public void CloseTab(TabItemViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (Tabs.Count == 0)
        {
            OpenNewQuotationTab();
            return;
        }

        if (ActiveTab == tab)
        {
            var newIndex = Math.Min(index, Tabs.Count - 1);
            ActivateTab(Tabs[newIndex]);
        }
    }

    /// <summary>编辑已有报价单（切换到新建报价页并加载数据）</summary>
    private void EditQuotation(string quotationId)
    {
        _newQuotationVM.LoadQuotation(quotationId);
        OpenNewQuotationTab();
    }

    // ============ 命令 ============

    /// <summary>切换深色/浅色模式</summary>
    [RelayCommand]
    private void ToggleDarkMode()
    {
        _themeService.Toggle();
        IsDarkMode = _themeService.IsDarkMode;
        _settingsVM.IsDarkMode = _themeService.IsDarkMode;
    }

    /// <summary>显示关于对话框</summary>
    private void ShowAboutDialog()
    {
        _dialog.ShowInfo(
            $"{AppInfo.GetVersionString()}\n\n技术栈：.NET 10 + WPF UI (Fluent Design) + SQLite",
            "关于 Quotix");
    }
}

/// <summary>标签页 ViewModel（每个打开的标签页对应一个实例）</summary>
public partial class TabItemViewModel : ObservableObject
{
    /// <summary>标签页唯一标识</summary>
    public string TabId { get; set; } = "";

    /// <summary>标签页标题</summary>
    public string Title { get; set; } = "";

    /// <summary>标签页绑定的内容（ViewModel）</summary>
    public object? Content { get; set; }

    /// <summary>标签页对应的视图（View）</summary>
    public System.Windows.FrameworkElement? View { get; set; }

    /// <summary>是否选中（控制标签页高亮）</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>是否允许关闭（设置页不允许关闭时可设为 false）</summary>
    public bool CanClose { get; set; } = true;
}
