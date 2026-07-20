using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotix.Models;
using Quotix.Services;

namespace Quotix.ViewModels;

/// <summary>
/// 表头数据库视图模型，负责负责人和客户信息的增删改查功能。
/// </summary>
public partial class HeaderDatabaseViewModel : ObservableObject
{
    private readonly HeaderService _headerService;
    private readonly DialogService _dialog;
    private readonly AppSettingsService _settingsService;

    /// <summary>
    /// 初始化 HeaderDatabaseViewModel 实例。
    /// </summary>
    /// <param name="headerService">表头服务</param>
    /// <param name="dialog">对话框服务</param>
    public HeaderDatabaseViewModel(
        HeaderService headerService,
        DialogService dialog,
        AppSettingsService settingsService)
    {
        _headerService = headerService;
        _dialog = dialog;
        _settingsService = settingsService;
        LoadSettings();
    }

    /// <summary>
    /// 无参构造函数，供 XAML 设计器使用（使用默认服务实例）。
    /// </summary>
    public HeaderDatabaseViewModel()
    {
        _headerService = new HeaderService(
            new Repositories.HeaderRepository(new Repositories.DatabaseProvider()),
            new Services.CacheService(
                new Repositories.ProductRepository(new Repositories.DatabaseProvider()),
                new Repositories.HeaderRepository(new Repositories.DatabaseProvider())));
        _dialog = new DialogService();
        _settingsService = new AppSettingsService();
        LoadSettings();
    }

    /// <summary>
    /// 是否显示负责人选项卡。
    /// </summary>
    [ObservableProperty] private bool _isOwnerTab = true;
    [ObservableProperty] private bool _isCustomerTab;
    [ObservableProperty] private bool _isQuotationDescriptionTab;
    [ObservableProperty] private string _defaultOwnerId = "";

    // 负责人字段
    /// <summary>
    /// 负责人集合。
    /// </summary>
    public ObservableCollection<Owner> Owners { get; } = new();
    [ObservableProperty] private string _ownerSearch = "";
    [ObservableProperty] private string _newOwnerName = "";
    [ObservableProperty] private string _newOwnerPhone = "";
    [ObservableProperty] private string _newOwnerTel = "";
    [ObservableProperty] private string _newOwnerEmail = "";

    // 客户字段
    /// <summary>
    /// 客户集合。
    /// </summary>
    public ObservableCollection<Customer> Customers { get; } = new();
    [ObservableProperty] private string _customerSearch = "";
    [ObservableProperty] private string _newCustomerName = "";
    [ObservableProperty] private string _newCustomerContact = "";
    [ObservableProperty] private string _newCustomerPhone = "";
    [ObservableProperty] private string _newCustomerEmail = "";

    // 报价说明默认值
    [ObservableProperty] private string _defaultValidity = "";
    [ObservableProperty] private string _defaultPayment = "";
    [ObservableProperty] private string _defaultDeliveryTime = "";
    [ObservableProperty] private string _defaultDeliveryMethod = "";

    private void LoadSettings()
    {
        DefaultOwnerId = _settingsService.DefaultOwnerId;
        var defaults = _settingsService.QuotationDescriptionDefaults;
        DefaultValidity = defaults.Validity;
        DefaultPayment = defaults.Payment;
        DefaultDeliveryTime = defaults.DeliveryTime;
        DefaultDeliveryMethod = defaults.DeliveryMethod;
    }

    /// <summary>
    /// 刷新当前选项卡的数据。
    /// </summary>
    public void Refresh()
    {
        _ = RefreshAsync();
    }

    /// <summary>
    /// 异步刷新数据（根据当前选项卡加载对应数据）。
    /// </summary>
    private async Task RefreshAsync()
    {
        if (IsOwnerTab)
            await RefreshOwnersAsync();
        else if (IsCustomerTab)
            await RefreshCustomersAsync();
    }

    /// <summary>
    /// 异步刷新负责人列表。
    /// </summary>
    private async Task RefreshOwnersAsync()
    {
        var list = await Task.Run(() => _headerService.GetOwners());
        Owners.Clear();
        foreach (var o in list)
            Owners.Add(o);
    }

    /// <summary>
    /// 异步刷新客户列表。
    /// </summary>
    private async Task RefreshCustomersAsync()
    {
        var list = await Task.Run(() => _headerService.GetCustomers());
        Customers.Clear();
        foreach (var c in list)
            Customers.Add(c);
    }

    /// <summary>
    /// 切换到负责人选项卡并加载数据。
    /// </summary>
    [RelayCommand]
    private async Task SwitchToOwner()
    {
        IsOwnerTab = true;
        IsCustomerTab = false;
        IsQuotationDescriptionTab = false;
        await RefreshOwnersAsync();
    }

    /// <summary>
    /// 切换到客户选项卡并加载数据。
    /// </summary>
    [RelayCommand]
    private async Task SwitchToCustomer()
    {
        IsOwnerTab = false;
        IsCustomerTab = true;
        IsQuotationDescriptionTab = false;
        await RefreshCustomersAsync();
    }

    /// <summary>
    /// 切换到报价说明默认值设置。
    /// </summary>
    [RelayCommand]
    private void SwitchToQuotationDescription()
    {
        IsOwnerTab = false;
        IsCustomerTab = false;
        IsQuotationDescriptionTab = true;
        LoadSettings();
    }

    /// <summary>
    /// 添加新的负责人。
    /// </summary>
    [RelayCommand]
    private async Task AddOwner()
    {
        if (string.IsNullOrWhiteSpace(NewOwnerName))
        {
            _dialog.ShowWarning("请输入负责人姓名");
            return;
        }

        _headerService.AddOwner(new Owner
        {
            Name = NewOwnerName,
            Phone = NewOwnerPhone,
            Tel = NewOwnerTel,
            Email = NewOwnerEmail
        });

        NewOwnerName = NewOwnerPhone = NewOwnerTel = NewOwnerEmail = "";
        await RefreshOwnersAsync();
    }

    /// <summary>
    /// 更新负责人信息。
    /// </summary>
    /// <param name="owner">要更新的负责人对象</param>
    [RelayCommand]
    private async Task UpdateOwner(Owner owner)
    {
        _headerService.UpdateOwner(owner);
        await RefreshOwnersAsync();
    }

    /// <summary>
    /// 删除指定负责人。
    /// </summary>
    /// <param name="id">负责人 ID</param>
    [RelayCommand]
    private async Task DeleteOwner(string id)
    {
        if (!_dialog.ShowConfirm("确定要删除此负责人吗？", "确认删除"))
            return;

        _headerService.DeleteOwner(id);
        if (DefaultOwnerId == id)
        {
            DefaultOwnerId = "";
            _settingsService.DefaultOwnerId = "";
        }
        await RefreshOwnersAsync();
    }

    /// <summary>
    /// 设置默认负责人，用于新建报价单自动填入报价方信息。
    /// </summary>
    [RelayCommand]
    private void SetDefaultOwner(string id)
    {
        DefaultOwnerId = id;
        _settingsService.DefaultOwnerId = id;
        var owner = Owners.FirstOrDefault(o => o.Id == id);
        _dialog.ShowInfo($"已设为默认负责人：{owner?.Name ?? "当前负责人"}", "成功");
    }

    /// <summary>
    /// 保存报价说明默认值。
    /// </summary>
    [RelayCommand]
    private void SaveQuotationDescriptionDefaults()
    {
        var defaults = _settingsService.QuotationDescriptionDefaults;
        defaults.Validity = DefaultValidity;
        defaults.Payment = DefaultPayment;
        defaults.DeliveryTime = DefaultDeliveryTime;
        defaults.DeliveryMethod = DefaultDeliveryMethod;
        _settingsService.SaveQuotationDescriptionDefaults();
        _dialog.ShowInfo("报价说明默认值已保存", "成功");
    }

    /// <summary>
    /// 恢复内置报价说明默认值。
    /// </summary>
    [RelayCommand]
    private void ResetQuotationDescriptionDefaults()
    {
        DefaultValidity = "1个月";
        DefaultPayment = "预付30%，发货前付全款";
        DefaultDeliveryTime = "8-12周";
        DefaultDeliveryMethod = "客户项目现场，含海运、内陆运输费用及相关保险费用";
    }

    /// <summary>
    /// 添加新的客户。
    /// </summary>
    [RelayCommand]
    private async Task AddCustomer()
    {
        if (string.IsNullOrWhiteSpace(NewCustomerName))
        {
            _dialog.ShowWarning("请输入客户名称");
            return;
        }

        _headerService.AddCustomer(new Customer
        {
            CompanyName = NewCustomerName,
            Contact = NewCustomerContact,
            Phone = NewCustomerPhone,
            Email = NewCustomerEmail
        });

        NewCustomerName = NewCustomerContact = NewCustomerPhone = NewCustomerEmail = "";
        await RefreshCustomersAsync();
    }

    /// <summary>
    /// 更新客户信息。
    /// </summary>
    /// <param name="customer">要更新的客户对象</param>
    [RelayCommand]
    private async Task UpdateCustomer(Customer customer)
    {
        _headerService.UpdateCustomer(customer);
        await RefreshCustomersAsync();
    }

    /// <summary>
    /// 删除指定客户。
    /// </summary>
    /// <param name="id">客户 ID</param>
    [RelayCommand]
    private async Task DeleteCustomer(string id)
    {
        if (!_dialog.ShowConfirm("确定要删除此客户吗？", "确认删除"))
            return;

        _headerService.DeleteCustomer(id);
        await RefreshCustomersAsync();
    }
}
