using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Quotix.Models;
using Quotix.ViewModels;

namespace Quotix.Views;

public partial class NewQuotationView : UserControl
{
    private NewQuotationViewModel VM => (NewQuotationViewModel)DataContext;
    private int _lastCodeRowIndex = -1;

    public NewQuotationView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not NewQuotationViewModel vm) return;
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(vm.IsQuickSearchVisible) && vm.IsQuickSearchVisible)
                    PositionQuickSearchPopup();
            };
        };
    }

    // ==================== Quick DB Toggle ====================

    private void QuickNDTButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not NewQuotationViewModel vm) return;
        vm.SwitchQuickDatabaseCommand.Execute("NDT");
    }

    private void QuickRVIButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not NewQuotationViewModel vm) return;
        vm.SwitchQuickDatabaseCommand.Execute("RVI");
    }

    // ==================== Code Field (Product Quick Input) ====================

    private void CodeBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && DataContext is NewQuotationViewModel vm)
        {
            // Find the row index by walking up the visual tree
            var parent = tb.Parent;
            while (parent != null)
            {
                if (parent is ContentPresenter cp)
                {
                    // ItemsControl generates ContentPresenter for each item
                    var itemsControl = ItemsControl.ItemsControlFromItemContainer(cp);
                    if (itemsControl != null)
                    {
                        var idx = itemsControl.ItemContainerGenerator.IndexFromContainer(cp);
                        if (idx >= 0 && (_lastCodeRowIndex != idx || vm.ActiveItemIndex != idx))
                        {
                            _lastCodeRowIndex = idx;
                            vm.OnCodeFieldFocused(idx);
                            // Re-trigger search
                            if (!string.IsNullOrEmpty(tb.Text))
                                vm.HandleQuickSearchTextChanged(tb.Text);
                        }
                        break;
                    }
                }
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent as System.Windows.DependencyObject);
            }
        }
    }

    private void CodeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && IsLoaded && DataContext is NewQuotationViewModel vm && vm.ActiveItemIndex >= 0)
            vm.HandleQuickSearchTextChanged(tb.Text);
    }

    // ==================== Owner / Customer Fields ====================

    private void OwnerBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || DataContext is not NewQuotationViewModel vm) return;
        vm.OnOwnerFieldFocused();
        if (sender is TextBox tb && !string.IsNullOrEmpty(tb.Text))
            vm.HandleQuickSearchTextChanged(tb.Text);
    }

    private void CustomerBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || DataContext is not NewQuotationViewModel vm) return;
        vm.OnCustomerFieldFocused();
        if (sender is TextBox tb && !string.IsNullOrEmpty(tb.Text))
            vm.HandleQuickSearchTextChanged(tb.Text);
    }

    private void QuickBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && IsLoaded && DataContext is NewQuotationViewModel vm)
            vm.HandleQuickSearchTextChanged(tb.Text);
    }

    // ==================== Lost Focus ====================

    private void QuickBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is NewQuotationViewModel vm)
            vm.OnQuickSearchLostFocus();
    }

    // ==================== Popup Position ====================

    private void PositionQuickSearchPopup()
    {
        if (DataContext is not NewQuotationViewModel vm) return;
        // Determine which element to place the popup near
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

    // ==================== Selection ====================

    private void QuickResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (QuickResultsList.SelectedItem is QuickSearchResult result && DataContext is NewQuotationViewModel vm)
            vm.OnQuickResultSelected(result);
    }

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
