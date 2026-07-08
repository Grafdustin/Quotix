using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Quotix.Models;
using Quotix.ViewModels;

namespace Quotix.Views;

/// <summary>
/// 新建报价单视图，负责处理报价单界面的用户交互。
/// </summary>
public partial class NewQuotationView : UserControl
{
    /// <summary>
    /// 获取当前数据上下文作为 NewQuotationViewModel。
    /// </summary>
    private NewQuotationViewModel VM => (NewQuotationViewModel)DataContext;
    private int _lastCodeRowIndex = -1;
    /// <summary>已订阅 PropertyChanged 的 VM 引用，用于切换/卸载时退订，避免重复订阅与 view 被 VM 强引用泄漏</summary>
    private NewQuotationViewModel? _subscribedVm;

    /// <summary>
    /// 初始化 NewQuotationView 实例。
    /// </summary>
    public NewQuotationView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 退订旧 VM，避免重复订阅与 view 被长生命周期 VM 强引用导致泄漏
        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
            _subscribedVm = null;
        }
        if (e.NewValue is NewQuotationViewModel vm)
        {
            _subscribedVm = vm;
            vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NewQuotationViewModel.IsQuickSearchVisible))
        {
            var visible = _subscribedVm?.IsQuickSearchVisible == true;
            // 先定位再打开，避免弹出瞬间 PlacementTarget 未就绪
            if (visible)
                PositionQuickSearchPopup();
            // 直接控制 Popup.IsOpen，避免绑定 + StaysOpen=False 自动关闭后
            // 本地值覆盖绑定、导致后续无法重新打开的经典陷阱
            QuickSearchPopup.IsOpen = visible;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
            _subscribedVm = null;
        }
    }

    // ==================== 快速数据库切换 ====================

    /// <summary>
    /// 切换到 NDT 数据库。
    /// </summary>
    private void QuickNDTButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not NewQuotationViewModel vm) return;
        vm.SwitchQuickDatabaseCommand.Execute("NDT");
    }

    /// <summary>
    /// 切换到 RVI 数据库。
    /// </summary>
    private void QuickRVIButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not NewQuotationViewModel vm) return;
        vm.SwitchQuickDatabaseCommand.Execute("RVI");
    }

    // ==================== 编码字段（产品快速输入） ====================

    /// <summary>
    /// 编码文本框获得焦点时调用，确定当前行索引并激活产品搜索模式。
    /// 聚焦即自动弹出快捷输入窗口（空文本也展示该列全部内容）。
    /// </summary>
    private void CodeBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && DataContext is NewQuotationViewModel vm)
        {
            // 文本框的数据上下文即为该行对应的报价项，直接定位行索引（比视觉树遍历更可靠）
            if (tb.DataContext is QuotationItemViewModel item)
            {
                var idx = vm.Items.IndexOf(item);
                if (idx >= 0)
                {
                    _lastCodeRowIndex = idx;
                    vm.OnCodeFieldFocused(idx);
                    // 聚焦即自动激活：空文本也展示全部，便于直接点选
                    vm.HandleQuickSearchTextChanged(tb.Text);
                }
            }
        }
    }

    /// <summary>
    /// 编码文本框文本变化时调用，触发快速搜索。
    /// </summary>
    private void CodeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && IsLoaded && DataContext is NewQuotationViewModel vm && vm.ActiveItemIndex >= 0)
            vm.HandleQuickSearchTextChanged(tb.Text);
    }

    // ==================== 负责人 / 客户字段 ====================

    /// <summary>
    /// 负责人文本框获得焦点时调用，激活负责人搜索模式。
    /// </summary>
    private void OwnerBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || DataContext is not NewQuotationViewModel vm) return;
        vm.OnOwnerFieldFocused();
        if (sender is TextBox tb && !string.IsNullOrEmpty(tb.Text))
            vm.HandleQuickSearchTextChanged(tb.Text);
    }

    /// <summary>
    /// 客户文本框获得焦点时调用，激活客户搜索模式。
    /// </summary>
    private void CustomerBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || DataContext is not NewQuotationViewModel vm) return;
        vm.OnCustomerFieldFocused();
        if (sender is TextBox tb && !string.IsNullOrEmpty(tb.Text))
            vm.HandleQuickSearchTextChanged(tb.Text);
    }

    /// <summary>
    /// 快速搜索文本框文本变化时调用。
    /// </summary>
    private void QuickBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && IsLoaded && DataContext is NewQuotationViewModel vm)
            vm.HandleQuickSearchTextChanged(tb.Text);
    }

    // ==================== 失去焦点 ====================

    /// <summary>
    /// 快速搜索失去焦点时调用。
    /// </summary>
    private void QuickBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is NewQuotationViewModel vm)
            vm.OnQuickSearchLostFocus();
    }

    // ==================== 弹出框定位 ====================

    /// <summary>
    /// 定位快速搜索弹出框，使其靠近当前激活的输入控件。
    /// </summary>
    private void PositionQuickSearchPopup()
    {
        if (DataContext is not NewQuotationViewModel vm) return;
        // 确定弹出框靠近哪个元素
        UIElement? placementTarget = null;

        if (vm.QuickSearchContext == "product" && vm.ActiveItemIndex >= 0)
        {
            var itemsControl = ItemsControlList;
            if (itemsControl == null) return;
            var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(vm.ActiveItemIndex);
            if (container is ContentPresenter cp)
            {
                placementTarget = FindVisualChild<TextBox>(cp);
            }
        }
        else if (vm.QuickSearchContext == "owner")
        {
            placementTarget = CompanyContactBox;
        }
        else if (vm.QuickSearchContext == "customer")
        {
            placementTarget = CustomerNameBox;
        }

        if (placementTarget != null)
        {
            QuickSearchPopup.PlacementTarget = placementTarget;
            QuickSearchPopup.HorizontalOffset = 0;
            QuickSearchPopup.VerticalOffset = 4;
        }
    }

    /// <summary>
    /// 在可视化树中查找指定类型的子元素。
    /// </summary>
    /// <typeparam name="T">要查找的元素类型</typeparam>
    /// <param name="parent">父元素</param>
    /// <returns>找到的第一个匹配元素，未找到则返回 null</returns>
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t)
                return t;
            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }

    // ==================== 选择结果 ====================

    /// <summary>
    /// 快速搜索结果列表鼠标左键释放时调用，选择搜索结果。
    /// </summary>
    private void QuickResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (QuickResultsList.SelectedItem is QuickSearchResult result && DataContext is NewQuotationViewModel vm)
            vm.OnQuickResultSelected(result);
    }

    /// <summary>
    /// 快速搜索结果列表按键时调用，处理回车和退出键。
    /// </summary>
    private void QuickResultsList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not NewQuotationViewModel vm) return;
        if (e.Key == Key.Enter && QuickResultsList.SelectedItem is QuickSearchResult result)
        {
            vm.OnQuickResultSelected(result);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.IsQuickSearchVisible = false;
            vm.QuickSearchResults.Clear();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 日期选择器加载时调用，调整弹出日历的对齐方式。
    /// </summary>
    private void QuoteDatePicker_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DatePicker dp) return;
        dp.ApplyTemplate();

        if (dp.Template.FindName("PART_Popup", dp) is not Popup popup) return;
        if (dp.Template.FindName("PART_Button", dp) is not Button btn) return;

        // 日历卡片右上角对齐按钮右下角
        popup.Opened += (_, _) =>
        {
            // 右边缘对齐
            popup.HorizontalOffset = -(popup.Child.RenderSize.Width - btn.ActualWidth);
        };
    }
}
