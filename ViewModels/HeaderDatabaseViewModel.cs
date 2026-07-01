using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotix.Models;
using Quotix.Services;

namespace Quotix.ViewModels;

public partial class HeaderDatabaseViewModel : ObservableObject
{
    private readonly HeaderService _headerService;
    private readonly DialogService _dialog;

    public HeaderDatabaseViewModel(HeaderService headerService, DialogService dialog)
    {
        _headerService = headerService;
        _dialog = dialog;
    }

    /// <summary>Parameterless ctor for XAML designer — uses default service instances.</summary>
    public HeaderDatabaseViewModel()
    {
        _headerService = new HeaderService(
            new Repositories.HeaderRepository(new Repositories.DatabaseProvider()),
            new Services.CacheService(
                new Repositories.ProductRepository(new Repositories.DatabaseProvider()),
                new Repositories.HeaderRepository(new Repositories.DatabaseProvider())));
        _dialog = new DialogService();
    }

    [ObservableProperty] private bool _isOwnerTab = true;

    // Owner fields
    public ObservableCollection<Owner> Owners { get; } = new();
    [ObservableProperty] private string _ownerSearch = "";
    [ObservableProperty] private string _newOwnerName = "";
    [ObservableProperty] private string _newOwnerPhone = "";
    [ObservableProperty] private string _newOwnerTel = "";
    [ObservableProperty] private string _newOwnerEmail = "";

    // Customer fields
    public ObservableCollection<Customer> Customers { get; } = new();
    [ObservableProperty] private string _customerSearch = "";
    [ObservableProperty] private string _newCustomerName = "";
    [ObservableProperty] private string _newCustomerContact = "";
    [ObservableProperty] private string _newCustomerPhone = "";
    [ObservableProperty] private string _newCustomerEmail = "";

    public void Refresh()
    {
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (IsOwnerTab)
            await RefreshOwnersAsync();
        else
            await RefreshCustomersAsync();
    }

    private async Task RefreshOwnersAsync()
    {
        var list = await Task.Run(() => _headerService.GetOwners());
        Owners.Clear();
        foreach (var o in list)
            Owners.Add(o);
    }

    private async Task RefreshCustomersAsync()
    {
        var list = await Task.Run(() => _headerService.GetCustomers());
        Customers.Clear();
        foreach (var c in list)
            Customers.Add(c);
    }

    [RelayCommand]
    private async Task SwitchToOwner()
    {
        IsOwnerTab = true;
        await RefreshOwnersAsync();
    }

    [RelayCommand]
    private async Task SwitchToCustomer()
    {
        IsOwnerTab = false;
        await RefreshCustomersAsync();
    }

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

    [RelayCommand]
    private async Task UpdateOwner(Owner owner)
    {
        _headerService.UpdateOwner(owner);
        await RefreshOwnersAsync();
    }

    [RelayCommand]
    private async Task DeleteOwner(string id)
    {
        if (!_dialog.ShowConfirm("确定要删除此负责人吗？", "确认删除"))
            return;

        _headerService.DeleteOwner(id);
        await RefreshOwnersAsync();
    }

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

    [RelayCommand]
    private async Task UpdateCustomer(Customer customer)
    {
        _headerService.UpdateCustomer(customer);
        await RefreshCustomersAsync();
    }

    [RelayCommand]
    private async Task DeleteCustomer(string id)
    {
        if (!_dialog.ShowConfirm("确定要删除此客户吗？", "确认删除"))
            return;

        _headerService.DeleteCustomer(id);
        await RefreshCustomersAsync();
    }
}
