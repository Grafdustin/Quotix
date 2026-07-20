using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Quotix.Models;
using Quotix.ViewModels;

namespace Quotix.Views;

/// <summary>
/// 表头数据库视图，负责负责人和客户信息的管理界面交互。
/// </summary>
public partial class HeaderDatabaseView : UserControl
{
    private HeaderDatabaseViewModel? _vm;
    private HeaderDatabaseViewModel VM => _vm!;

    private ObservableCollection<Owner>? _allOwners;
    private ObservableCollection<Customer>? _allCustomers;

    /// <summary>
    /// 初始化 HeaderDatabaseView 实例。
    /// </summary>
    public HeaderDatabaseView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// DataContext 变化时调用，处理 ViewModel 的绑定与解绑。
    /// </summary>
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

    /// <summary>
    /// 视图加载时调用，重新绑定 ViewModel 并更新记录数显示。
    /// </summary>
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

    /// <summary>
    /// 视图卸载时调用，清理事件订阅。
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachVM();
    }

    /// <summary>
    /// 绑定 ViewModel 的事件订阅并缓存数据。
    /// </summary>
    /// <param name="vm">要绑定的 ViewModel</param>
    private void AttachVM(HeaderDatabaseViewModel vm)
    {
        vm.Owners.CollectionChanged += OnOwnersChanged;
        vm.Customers.CollectionChanged += OnCustomersChanged;
        vm.PropertyChanged += OnVMPropertyChanged;

        CacheAllOwners(vm);
        CacheAllCustomers(vm);
        UpdateRecordCount();
    }

    /// <summary>
    /// 解绑 ViewModel 的事件订阅。
    /// </summary>
    private void DetachVM()
    {
        if (_vm == null) return;

        _vm.Owners.CollectionChanged -= OnOwnersChanged;
        _vm.Customers.CollectionChanged -= OnCustomersChanged;
        _vm.PropertyChanged -= OnVMPropertyChanged;
        _vm = null;
    }

    /// <summary>
    /// 负责人集合变化时调用，重新缓存数据并更新记录数。
    /// </summary>
    private void OnOwnersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        CacheAllOwners(VM);
        UpdateRecordCount();
    }

    /// <summary>
    /// 客户集合变化时调用，重新缓存数据并更新记录数。
    /// </summary>
    private void OnCustomersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        CacheAllCustomers(VM);
        UpdateRecordCount();
    }

    /// <summary>
    /// ViewModel 属性变化时调用，处理选项卡切换时的搜索框清空。
    /// </summary>
    private void OnVMPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VM.IsOwnerTab))
        {
            SearchBox.Text = "";
            UpdateRecordCount();
        }
        else if (e.PropertyName == nameof(VM.IsCustomerTab) ||
                 e.PropertyName == nameof(VM.IsQuotationDescriptionTab))
        {
            SearchBox.Text = "";
            UpdateRecordCount();
        }
    }

    /// <summary>
    /// 缓存所有负责人数据（用于前端搜索过滤）。
    /// </summary>
    /// <param name="vm">当前 ViewModel</param>
    private void CacheAllOwners(HeaderDatabaseViewModel vm)
    {
        _allOwners = new ObservableCollection<Owner>(vm.Owners);
    }

    /// <summary>
    /// 缓存所有客户数据（用于前端搜索过滤）。
    /// </summary>
    /// <param name="vm">当前 ViewModel</param>
    private void CacheAllCustomers(HeaderDatabaseViewModel vm)
    {
        _allCustomers = new ObservableCollection<Customer>(vm.Customers);
    }

    // ══════════════════════════════════════════════════════
    //  搜索过滤
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 搜索框文本变化时调用，根据当前选项卡过滤数据。
    /// </summary>
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_vm == null || VM.IsQuotationDescriptionTab) return;

        var search = SearchBox.Text?.Trim() ?? "";

        if (VM.IsOwnerTab)
            FilterOwners(search);
        else
            FilterCustomers(search);
    }

    /// <summary>
    /// 根据搜索文本过滤负责人列表。
    /// </summary>
    /// <param name="search">搜索文本</param>
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

    /// <summary>
    /// 根据搜索文本过滤客户列表。
    /// </summary>
    /// <param name="search">搜索文本</param>
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

    /// <summary>
    /// 更新记录数显示。
    /// </summary>
    private void UpdateRecordCount()
    {
        if (_vm == null) return;
        RecordCountRun.Text = VM.IsOwnerTab
            ? VM.Owners.Count.ToString("N0")
            : VM.IsCustomerTab ? VM.Customers.Count.ToString("N0") : "0";
    }

    // ══════════════════════════════════════════════════════
    //  切换
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 切换到负责人选项卡。
    /// </summary>
    private void SwitchToOwner(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        VM.SwitchToOwnerCommand.Execute(null);
    }

    /// <summary>
    /// 切换到客户选项卡。
    /// </summary>
    private void SwitchToCustomer(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        VM.SwitchToCustomerCommand.Execute(null);
    }

    /// <summary>
    /// 切换到报价说明默认值设置。
    /// </summary>
    private void SwitchToQuotationDescription(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        VM.SwitchToQuotationDescriptionCommand.Execute(null);
    }

    // ══════════════════════════════════════════════════════
    //  删除
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 删除负责人按钮点击事件。
    /// </summary>
    private void DeleteOwnerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Owner owner)
        {
            VM.DeleteOwnerCommand.Execute(owner.Id);
        }
    }

    /// <summary>
    /// 设置默认负责人按钮点击事件。
    /// </summary>
    private void SetDefaultOwnerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Owner owner)
        {
            VM.SetDefaultOwnerCommand.Execute(owner.Id);
        }
    }

    /// <summary>打开负责人编辑弹窗。</summary>
    private void EditOwnerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Owner owner })
        {
            VM.BeginEditOwner(owner);
            AddDialogOverlay.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// 删除客户按钮点击事件。
    /// </summary>
    private void DeleteCustomerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Customer customer)
        {
            VM.DeleteCustomerCommand.Execute(customer.Id);
        }
    }

    /// <summary>打开客户编辑弹窗。</summary>
    private void EditCustomerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Customer customer })
        {
            VM.BeginEditCustomer(customer);
            AddDialogOverlay.Visibility = Visibility.Visible;
        }
    }

    // ══════════════════════════════════════════════════════
    //  新建弹窗（覆盖式居中卡片）
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 新建按钮点击事件，显示新建对话框。
    /// </summary>
    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        if (VM.IsOwnerTab)
            VM.BeginNewOwner();
        else
            VM.BeginNewCustomer();

        AddDialogOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 取消新建按钮点击事件，关闭对话框。
    /// </summary>
    private void PopupCancel_Click(object sender, RoutedEventArgs e)
    {
        VM.CancelEntryEditing();
        AddDialogOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 确认新建负责人按钮点击事件。
    /// </summary>
    private async void PopupAddOwner_Click(object sender, RoutedEventArgs e)
    {
        if (VM.IsEditingOwner)
        {
            await VM.UpdateOwnerCommand.ExecuteAsync(new Owner
            {
                Id = VM.EditingOwnerId,
                Name = VM.NewOwnerName,
                Phone = VM.NewOwnerPhone,
                Tel = VM.NewOwnerTel,
                Email = VM.NewOwnerEmail
            });
        }
        else
        {
            await VM.AddOwnerCommand.ExecuteAsync(null);
        }

        AddDialogOverlay.Visibility = Visibility.Collapsed;

        CacheAllOwners(VM);
        UpdateRecordCount();
    }

    /// <summary>
    /// 确认新建客户按钮点击事件。
    /// </summary>
    private async void PopupAddCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (VM.IsEditingCustomer)
        {
            await VM.UpdateCustomerCommand.ExecuteAsync(new Customer
            {
                Id = VM.EditingCustomerId,
                CompanyName = VM.NewCustomerName,
                Contact = VM.NewCustomerContact,
                Phone = VM.NewCustomerPhone,
                Email = VM.NewCustomerEmail
            });
        }
        else
        {
            await VM.AddCustomerCommand.ExecuteAsync(null);
        }

        AddDialogOverlay.Visibility = Visibility.Collapsed;

        CacheAllCustomers(VM);
        UpdateRecordCount();
    }
}
