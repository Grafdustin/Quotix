using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace Quotix.Views;

public partial class PriceCalculatorDialog : FluentWindow
{
    private readonly List<CalculatorItem> _items;
    private readonly List<CalculatorItem> _backup;

    public List<CalculatorItem> ResultItems { get; private set; }

    public PriceCalculatorDialog(List<ViewModels.QuotationItemViewModel> items)
    {
        InitializeComponent();

        _items = items.Select(i => new CalculatorItem
        {
            ItemName = i.ItemName,
            UnitPrice = i.UnitPrice,
            OriginalPrice = i.OriginalPrice
        }).ToList();

        _backup = _items.Select(i => new CalculatorItem
        {
            ItemName = i.ItemName,
            UnitPrice = i.UnitPrice,
            OriginalPrice = i.OriginalPrice
        }).ToList();

        ResultItems = new List<CalculatorItem>(_items);
        ItemsControl.ItemsSource = new ObservableCollection<CalculatorItem>(_items);
    }

    private void NumberPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !decimal.TryParse(e.Text, out _);
    }

    private void CalculateClick(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(ValueInput.Text, out var value) || value == 0) return;

        var isMultiply = OperationCombo.SelectedIndex == 0;
        var roundMode = RoundModeCombo.SelectedIndex; // 0=none, 1=ceil, 2=floor, 3=round

        foreach (var item in _items)
        {
            item.OriginalPrice = item.UnitPrice.ToString("F2");

            var result = isMultiply ? item.UnitPrice * value : item.UnitPrice / value;

            item.UnitPrice = roundMode switch
            {
                1 => Math.Ceiling(result),
                2 => Math.Floor(result),
                3 => Math.Round(result, MidpointRounding.AwayFromZero),
                _ => Math.Round(result, 2)
            };
        }

        // Refresh display
        ItemsControl.ItemsSource = null;
        ItemsControl.ItemsSource = new ObservableCollection<CalculatorItem>(_items);
    }

    private void UndoClick(object sender, RoutedEventArgs e)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            _items[i].UnitPrice = _backup[i].UnitPrice;
            _items[i].OriginalPrice = _backup[i].OriginalPrice;
        }

        ItemsControl.ItemsSource = null;
        ItemsControl.ItemsSource = new ObservableCollection<CalculatorItem>(_items);
    }

    private void ApplyClick(object sender, RoutedEventArgs e)
    {
        ResultItems = new List<CalculatorItem>(_items);
        DialogResult = true;
        Close();
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public class CalculatorItem : INotifyPropertyChanged
{
    private string _itemName = "";
    private decimal _unitPrice;
    private string _originalPrice = "";

    public string ItemName
    {
        get => _itemName;
        set { _itemName = value; OnPropertyChanged(nameof(ItemName)); }
    }

    public decimal UnitPrice
    {
        get => _unitPrice;
        set { _unitPrice = value; OnPropertyChanged(nameof(UnitPrice)); }
    }

    public string OriginalPrice
    {
        get => _originalPrice;
        set { _originalPrice = value; OnPropertyChanged(nameof(OriginalPrice)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
