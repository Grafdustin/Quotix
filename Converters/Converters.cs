using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace Quotix.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) 
        => throw new NotImplementedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class MultiplyConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && decimal.TryParse(values[0]?.ToString(), out var a) && decimal.TryParse(values[1]?.ToString(), out var b))
            return (a * b).ToString("F2");
        return "0.00";
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value?.ToString()) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// RMB/USD 按钮高亮
public class CurrencyBgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var target = parameter?.ToString() ?? "RMB";
        return value?.ToString() == target
            ? ControlAppearance.Primary
            : ControlAppearance.Secondary;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// 快速输入数据库切换高亮 (NDT / RVI)
public class QuickDbAppearanceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var target = parameter?.ToString() ?? "";
        return value?.ToString() == target
            ? ControlAppearance.Primary
            : ControlAppearance.Secondary;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ItemsControl 行号索引 (使用 ContentPresenter)
public class IndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ContentPresenter cp)
        {
            var ic = ItemsControl.ItemsControlFromItemContainer(cp);
            if (ic != null)
                return (ic.ItemContainerGenerator.IndexFromContainer(cp) + 1).ToString();
        }
        return "?";
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// decimal 为 0 时显示空字符串
public class ZeroToEmptyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d && d == 0m)
            return string.Empty;
        return value?.ToString() ?? string.Empty;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(value?.ToString()))
            return 0m;
        if (decimal.TryParse(value?.ToString(), out var result))
            return result;
        return 0m;
    }
}
