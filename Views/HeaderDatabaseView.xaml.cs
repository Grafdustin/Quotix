using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Quotix.Models;
using Quotix.ViewModels;

namespace Quotix.Views;

public partial class HeaderDatabaseView : UserControl
{
    private HeaderDatabaseViewModel? _vm;
    private HeaderDatabaseViewModel VM => _vm!;

    private ObservableCollection<Owner>? _allOwners;
    private ObservableCollection<Customer>? _allCustomers;

    public HeaderDatabaseView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is HeaderDatabaseViewModel vm)
        {
            _vm = vm;
            AttachVM(vm);
        }
        else
        {
            DetachVM();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // View 缓存后重新挂载时，DataContext 可能未变但事件已丢失
        if (DataContext is HeaderDatabaseViewModel vm && _vm == null)
        {
            _vm = vm;
            AttachVM(vm);
        }
        UpdateRecordCount();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachVM();
    }

    private void AttachVM(HeaderDatabaseViewModel vm)
    {
        vm.Owners.CollectionChanged += OnOwnersChanged;
        vm.Customers.CollectionChanged += OnCustomersChanged;
        vm.PropertyChanged += OnVMPropertyChanged;

        CacheAllOwners(vm);
        CacheAllCustomers(vm);
        UpdateRecordCount();
    }

    private void DetachVM()
    {
        if (_vm == null) return;

        _vm.Owners.CollectionChanged -= OnOwnersChanged;
        _vm.Customers.CollectionChanged -= OnCustomersChanged;
        _vm.PropertyChanged -= OnVMPropertyChanged;
        _vm = null;
    }

    private void OnOwnersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        CacheAllOwners(VM);
        UpdateRecordCount();
    }

    private void OnCustomersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        CacheAllCustomers(VM);
        UpdateRecordCount();
    }

    private void OnVMPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VM.IsOwnerTab))
        {
            SearchBox.Text = "";
            UpdateRecordCount();
        }
    }

    private void CacheAllOwners(HeaderDatabaseViewModel vm)
    {
        _allOwners = new ObservableCollection<Owner>(vm.Owners);
    }

    private void CacheAllCustomers(HeaderDatabaseViewModel vm)
    {
        _allCustomers = new ObservableCollection<Customer>(vm.Customers);
    }

    // ═══════════════════════════════════════════════════════
    //  搜索过滤
    // ═══════════════════════════════════════════════════════

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var search = SearchBox.Text?.Trim() ?? "";

        if (VM.IsOwnerTab)
            FilterOwners(search);
        else
            FilterCustomers(search);
    }

    private void FilterOwners(string search)
    {
        if (_allOwners == null) return;

        if (string.IsNullOrEmpty(search))
        {
            VM.Owners.Clear();
            foreach (var o in _allOwners)
                VM.Owners.Add(o);
        }
        else
        {
            var filtered = _allOwners
                .Where(o => (o.Name?.Contains(search, System.StringComparison.OrdinalIgnoreCase) ?? false)
                         || (o.Phone?.Contains(search, System.StringComparison.OrdinalIgnoreCase) ?? false)
                         || (o.Tel?.Contains(search, System.StringComparison.OrdinalIgnoreCase) ?? false)
                         || (o.Email?.Contains(search, System.StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();

            VM.Owners.Clear();
            foreach (var o in filtered)
                VM.Owners.Add(o);
        }

        UpdateRecordCount();
    }

    private void FilterCustomers(string search)
    {
        if (_allCustomers == null) return;

        if (string.IsNullOrEmpty(search))
        {
            VM.Customers.Clear();
            foreach (var c in _allCustomers)
                VM.Customers.Add(c);
        }
        else
        {
            var filtered = _allCustomers
                .Where(c => (c.CompanyName?.Contains(search, System.StringComparison.OrdinalIgnoreCase) ?? false)
                         || (c.Contact?.Contains(search, System.StringComparison.OrdinalIgnoreCase) ?? false)
                         || (c.Phone?.Contains(search, System.StringComparison.OrdinalIgnoreCase) ?? false)
                         || (c.Email?.Contains(search, System.StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();

            VM.Customers.Clear();
            foreach (var c in filtered)
                VM.Customers.Add(c);
        }

        UpdateRecordCount();
    }

    private void UpdateRecordCount()
    {
        if (_vm == null) return;
        RecordCountRun.Text = VM.IsOwnerTab
            ? VM.Owners.Count.ToString("N0")
            : VM.Customers.Count.ToString("N0");
    }

    // ═══════════════════════════════════════════════════════
    //  切换
    // ═══════════════════════════════════════════════════════

    private void SwitchToOwner(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        VM.SwitchToOwnerCommand.Execute(null);
    }

    private void SwitchToCustomer(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        VM.SwitchToCustomerCommand.Execute(null);
    }

    // ═══════════════════════════════════════════════════════
    //  删除
    // ═══════════════════════════════════════════════════════

    private void DeleteOwnerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Owner owner)
        {
            VM.DeleteOwnerCommand.Execute(owner.Id);
        }
    }

    private void DeleteCustomerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Customer customer)
        {
            VM.DeleteCustomerCommand.Execute(customer.Id);
        }
    }

    // ═══════════════════════════════════════════════════════
    //  新建弹窗（覆盖式居中卡片）
    // ═══════════════════════════════════════════════════════

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        // 清空上次填写的内容
        VM.NewOwnerName = VM.NewOwnerPhone = VM.NewOwnerTel = VM.NewOwnerEmail = "";
        VM.NewCustomerName = VM.NewCustomerContact = VM.NewCustomerPhone = VM.NewCustomerEmail = "";

        AddDialogOverlay.Visibility = Visibility.Visible;
    }

    private void PopupCancel_Click(object sender, RoutedEventArgs e)
    {
        AddDialogOverlay.Visibility = Visibility.Collapsed;
    }

    private async void PopupAddOwner_Click(object sender, RoutedEventArgs e)
    {
        AddDialogOverlay.Visibility = Visibility.Collapsed;
        await VM.AddOwnerCommand.ExecuteAsync(null);

        CacheAllOwners(VM);
        UpdateRecordCount();
    }

    private async void PopupAddCustomer_Click(object sender, RoutedEventArgs e)
    {
        AddDialogOverlay.Visibility = Visibility.Collapsed;
        await VM.AddCustomerCommand.ExecuteAsync(null);

        CacheAllCustomers(VM);
        UpdateRecordCount();
    }
}
