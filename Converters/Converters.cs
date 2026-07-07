using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace Quotix.Converters;

/// <summary>
/// 布尔值转可见性转换器。当值为 true 时返回 Visible，否则返回 Collapsed。
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) 
        => throw new NotImplementedException();
}

/// <summary>
/// 反布尔值转可见性转换器。当值为 true 时返回 Collapsed，否则返回 Visible。
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 乘法转换器，将两个值相乘并返回格式化字符串。
/// </summary>
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

/// <summary>
/// 字符串转可见性转换器。当字符串为空时返回 Collapsed，否则返回 Visible。
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value?.ToString()) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 币种按钮背景转换器。当前币种与目标币种匹配时返回 Primary，否则返回 Secondary。
/// </summary>
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

/// <summary>
/// 快速输入数据库切换高亮转换器。当前数据库与目标数据库匹配时返回 Primary，否则返回 Secondary。
/// </summary>
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

/// <summary>
/// ItemsControl 行号索引转换器。根据 ContentPresenter 获取其在 ItemsControl 中的索引（从 1 开始）。
/// </summary>
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

/// <summary>
/// 设置分类可见性转换器。当绑定值（当前选中分类 Key）与参数相等时返回 Visible，否则返回 Collapsed。
/// </summary>
public class CategoryToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 零值转空字符串转换器。当 decimal 值为 0 时返回空字符串，否则返回原值。
/// </summary>
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
