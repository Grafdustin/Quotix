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

public partial class MainViewModel : ObservableObject
{
    private readonly AppSettingsService _appSettings;
    private readonly ThemeService _themeService;
    private readonly DialogService _dialog;
    private readonly NewQuotationViewModel _newQuotationVM;
    private readonly SettingsViewModel _settingsVM;
    
    public SettingsViewModel SettingsViewModel => _settingsVM;

    // ===== Tabs =====
    public ObservableCollection<TabItemViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private TabItemViewModel? _activeTab;

    public object? ActiveTabContent => ActiveTab?.View;

    partial void OnActiveTabChanged(TabItemViewModel? value)
    {
        OnPropertyChanged(nameof(ActiveTabContent));
    }

    [ObservableProperty] private string _statusText = "就绪";

    // ===== Progress =====
    [ObservableProperty] private bool _isProgressVisible;
    [ObservableProperty] private double _progressPercentage;
    [ObservableProperty] private string _progressText = "请稍候...";

    // ===== Dark Mode =====
    [ObservableProperty] private bool _isDarkMode;

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

        // Messenger 订阅（替代事件耦合）
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

        _themeService.Load();
        IsDarkMode = _themeService.IsDarkMode;
        _settingsVM.IsDarkMode = _themeService.IsDarkMode;

        // 默认打开新建报价标签页
        OpenNewQuotationTab();
    }

    // 预注入的可复用 VM（每次开标签页复用同一实例）
    private readonly ProductDatabaseViewModel _productDbVM;
    private readonly HeaderDatabaseViewModel _headerDbVM;
    private readonly HistoryViewModel _historyVM;

    // ===== Tab Management =====

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

    public void ActivateTab(TabItemViewModel tab)
    {
        foreach (var t in Tabs)
            t.IsSelected = false;
        tab.IsSelected = true;
        ActiveTab = tab;
    }

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

    private void EditQuotation(string quotationId)
    {
        _newQuotationVM.LoadQuotation(quotationId);
        OpenNewQuotationTab();
    }

    [RelayCommand]
    private void ToggleDarkMode()
    {
        _themeService.Toggle();
        IsDarkMode = _themeService.IsDarkMode;
        _settingsVM.IsDarkMode = _themeService.IsDarkMode;
    }

    private void ShowAboutDialog()
    {
        _dialog.ShowInfo(
            "Quotix v3.0\n\n" +
            "技术栈：.NET 10 + WPF UI (Fluent Design) + SQLite",
            "关于 Quotix");
    }
}

public partial class TabItemViewModel : ObservableObject
{
    public string TabId { get; set; } = "";
    public string Title { get; set; } = "";
    public object? Content { get; set; }
    public System.Windows.FrameworkElement? View { get; set; }

    [ObservableProperty] private bool _isSelected;
    public bool CanClose { get; set; } = true;
}
