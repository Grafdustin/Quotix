using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace Quotix.Views;

/// <summary>
/// 价格计算器对话框，支持对报价项进行批量乘法或除法计算。
/// </summary>
public partial class PriceCalculatorDialog : FluentWindow
{
    private readonly List<CalculatorItem> _items;
    private readonly List<CalculatorItem> _backup;

    /// <summary>
    /// 计算后的结果项列表。
    /// </summary>
    public List<CalculatorItem> ResultItems { get; private set; }

    /// <summary>
    /// 初始化 PriceCalculatorDialog 实例。
    /// </summary>
    /// <param name="items">要处理的报价项列表</param>
    public PriceCalculatorDialog(List<ViewModels.QuotationItemViewModel> items)
    {
        InitializeComponent();

        _items = items.Select(i => new CalculatorItem
        {
            ItemName = i.ItemName,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice,
            OriginalPrice = i.OriginalPrice
        }).ToList();

        _backup = _items.Select(i => new CalculatorItem
        {
            ItemName = i.ItemName,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice,
            OriginalPrice = i.OriginalPrice
        }).ToList();

        ResultItems = new List<CalculatorItem>(_items);
        ItemsControl.ItemsSource = new ObservableCollection<CalculatorItem>(_items);
        RefreshTotals();
    }

    /// <summary>
    /// 在窗口空白区域按住左键时拖动计算器窗口。
    /// </summary>
    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsInteractiveElement(e.OriginalSource as DependencyObject))
            return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 鼠标状态在拖动开始前变化时忽略，避免影响弹窗操作。
        }
    }

    /// <summary>
    /// 限制输入只能为数字和小数点。
    /// </summary>
    private void NumberPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb)
            return;

        var text = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength)
            .Insert(tb.SelectionStart, e.Text);
        e.Handled = !IsValidNumberText(text);
    }

    private static bool IsValidNumberText(string text)
    {
        return string.IsNullOrWhiteSpace(text) ||
               text == "." ||
               decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out _);
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase ||
                source is System.Windows.Controls.TextBox ||
                source is ComboBox)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    /// <summary>
    /// 计算按钮点击事件，根据选择的操作和取整模式计算单价。
    /// </summary>
    private void CalculateClick(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(ValueInput.Text, out var value))
            return;

        var operation = (OperationCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "×";

        if (operation == "÷" && value == 0)
        {
            return;
        }
        var roundMode = RoundModeCombo.SelectedIndex; // 0=无取整, 1=向上取整, 2=向下取整, 3=四舍五入
        foreach (var item in _items)
        {
            item.OriginalPrice = item.UnitPrice.ToString("F2");

            var result = operation switch
            {
                "×" => item.UnitPrice * value,
                "÷" => item.UnitPrice / value,
                "+" => item.UnitPrice + value,
                "-" => item.UnitPrice - value,
                _ => item.UnitPrice
            };

            item.UnitPrice = roundMode switch
            {
                1 => Math.Ceiling(result),
                2 => Math.Floor(result),
                3 => Math.Round(result, MidpointRounding.AwayFromZero),
                _ => Math.Round(result, 2)
            };
        }

        // 刷新显示
        ItemsControl.ItemsSource = null;
        ItemsControl.ItemsSource = new ObservableCollection<CalculatorItem>(_items);
        RefreshTotals();
    }

    /// <summary>
    /// 撤销按钮点击事件，恢复原始价格。
    /// </summary>
    private void UndoClick(object sender, RoutedEventArgs e)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            _items[i].UnitPrice = _backup[i].UnitPrice;
            _items[i].OriginalPrice = _backup[i].OriginalPrice;
        }

        ItemsControl.ItemsSource = null;
        ItemsControl.ItemsSource = new ObservableCollection<CalculatorItem>(_items);
        RefreshTotals();
    }

    private void RefreshTotals()
    {
        var originalTotal = _items.Sum(i => i.Quantity * i.GetOriginalUnitPriceForTotal());
        var newTotal = _items.Sum(i => i.Quantity * i.UnitPrice);

        OriginalTotalText.Text = originalTotal.ToString("N2", CultureInfo.CurrentCulture);
        NewTotalText.Text = newTotal.ToString("N2", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// 应用按钮点击事件，保存结果并关闭对话框。
    /// </summary>
    private void ApplyClick(object sender, RoutedEventArgs e)
    {
        ResultItems = new List<CalculatorItem>(_items);
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// 取消按钮点击事件，关闭对话框。
    /// </summary>
    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

/// <summary>
/// 计算器项，表示价格计算器中的一个产品条目。
/// </summary>
public class CalculatorItem : INotifyPropertyChanged
{
    private string _itemName = "";
    private decimal _unitPrice;
    private int _quantity = 1;
    private string _originalPrice = "";

    /// <summary>
    /// 产品名称。
    /// </summary>
    public string ItemName
    {
        get => _itemName;
        set { _itemName = value; OnPropertyChanged(nameof(ItemName)); }
    }

    /// <summary>
    /// 单价。
    /// </summary>
    public decimal UnitPrice
    {
        get => _unitPrice;
        set { _unitPrice = value; OnPropertyChanged(nameof(UnitPrice)); }
    }

    /// <summary>
    /// 数量。
    /// </summary>
    public int Quantity
    {
        get => _quantity;
        set { _quantity = value; OnPropertyChanged(nameof(Quantity)); }
    }

    /// <summary>
    /// 原始价格（计算前）。
    /// </summary>
    public string OriginalPrice
    {
        get => _originalPrice;
        set { _originalPrice = value; OnPropertyChanged(nameof(OriginalPrice)); }
    }

    public decimal GetOriginalUnitPriceForTotal()
    {
        return decimal.TryParse(OriginalPrice, NumberStyles.Number, CultureInfo.CurrentCulture, out var price)
            ? price
            : UnitPrice;
    }

    /// <summary>
    /// 属性变更事件。
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
