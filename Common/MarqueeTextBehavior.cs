using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Quotix.Common;

/// <summary>
/// TextBlock 超出容器宽度时横向轮播显示完整文本。
/// </summary>
public static class MarqueeTextBehavior
{
    private const double EdgePadding = 10;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(MarqueeTextBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static readonly DependencyProperty OriginalHorizontalAlignmentProperty =
        DependencyProperty.RegisterAttached(
            "OriginalHorizontalAlignment",
            typeof(HorizontalAlignment),
            typeof(MarqueeTextBehavior),
            new PropertyMetadata(HorizontalAlignment.Stretch));

    private static readonly DependencyProperty OriginalTextAlignmentProperty =
        DependencyProperty.RegisterAttached(
            "OriginalTextAlignment",
            typeof(TextAlignment),
            typeof(MarqueeTextBehavior),
            new PropertyMetadata(TextAlignment.Left));

    private static readonly DependencyProperty OriginalWidthProperty =
        DependencyProperty.RegisterAttached(
            "OriginalWidth",
            typeof(double),
            typeof(MarqueeTextBehavior),
            new PropertyMetadata(double.NaN));

    private static readonly DependencyProperty OriginalMinWidthProperty =
        DependencyProperty.RegisterAttached(
            "OriginalMinWidth",
            typeof(double),
            typeof(MarqueeTextBehavior),
            new PropertyMetadata(0d));

    private static readonly DependencyProperty HasOriginalAlignmentProperty =
        DependencyProperty.RegisterAttached(
            "HasOriginalAlignment",
            typeof(bool),
            typeof(MarqueeTextBehavior),
            new PropertyMetadata(false));

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
            return;

        if ((bool)e.NewValue)
        {
            textBlock.Loaded += OnLoaded;
            textBlock.SizeChanged += OnSizeChanged;
            DependencyPropertyDescriptorHelper.AddValueChanged(textBlock, TextBlock.TextProperty, OnTextChanged);
            Update(textBlock);
        }
        else
        {
            textBlock.Loaded -= OnLoaded;
            textBlock.SizeChanged -= OnSizeChanged;
            DependencyPropertyDescriptorHelper.RemoveValueChanged(textBlock, TextBlock.TextProperty, OnTextChanged);
            Stop(textBlock);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBlock textBlock)
            Update(textBlock);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is TextBlock textBlock)
            Update(textBlock);
    }

    private static void OnTextChanged(object? sender, EventArgs e)
    {
        if (sender is TextBlock textBlock)
            Update(textBlock);
    }

    private static void Update(TextBlock textBlock)
    {
        if (!textBlock.IsLoaded)
            return;

        textBlock.Dispatcher.BeginInvoke(() =>
        {
            var viewport = GetViewportWidth(textBlock);
            if (viewport <= 0)
            {
                Stop(textBlock);
                return;
            }

            var textWidth = MeasureTextWidth(textBlock) + EdgePadding;
            var overflow = textWidth - viewport;
            if (overflow <= 2)
            {
                Stop(textBlock);
                return;
            }

            var transform = textBlock.RenderTransform as TranslateTransform;
            if (transform == null)
            {
                transform = new TranslateTransform();
                textBlock.RenderTransform = transform;
            }

            RememberAlignment(textBlock);
            textBlock.MinWidth = textWidth;
            textBlock.Width = textWidth;
            textBlock.HorizontalAlignment = HorizontalAlignment.Left;
            textBlock.TextAlignment = TextAlignment.Left;
            textBlock.InvalidateMeasure();
            textBlock.UpdateLayout();
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = 0;

            var duration = TimeSpan.FromSeconds(Math.Clamp(overflow / 34, 1.6, 4.2));
            var animation = new DoubleAnimation
            {
                From = 0,
                To = -overflow,
                BeginTime = TimeSpan.FromSeconds(0.45),
                Duration = duration,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            transform.BeginAnimation(TranslateTransform.XProperty, animation);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static void Stop(TextBlock textBlock)
    {
        if (textBlock.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = 0;
        }

        RestoreAlignment(textBlock);
    }

    private static void RememberAlignment(TextBlock textBlock)
    {
        if ((bool)textBlock.GetValue(HasOriginalAlignmentProperty))
            return;

        textBlock.SetValue(OriginalHorizontalAlignmentProperty, textBlock.HorizontalAlignment);
        textBlock.SetValue(OriginalTextAlignmentProperty, textBlock.TextAlignment);
        textBlock.SetValue(OriginalWidthProperty, textBlock.Width);
        textBlock.SetValue(OriginalMinWidthProperty, textBlock.MinWidth);
        textBlock.SetValue(HasOriginalAlignmentProperty, true);
    }

    private static void RestoreAlignment(TextBlock textBlock)
    {
        if (!(bool)textBlock.GetValue(HasOriginalAlignmentProperty))
            return;

        textBlock.HorizontalAlignment = (HorizontalAlignment)textBlock.GetValue(OriginalHorizontalAlignmentProperty);
        textBlock.TextAlignment = (TextAlignment)textBlock.GetValue(OriginalTextAlignmentProperty);
        textBlock.Width = (double)textBlock.GetValue(OriginalWidthProperty);
        textBlock.MinWidth = (double)textBlock.GetValue(OriginalMinWidthProperty);
        textBlock.SetValue(HasOriginalAlignmentProperty, false);
    }

    private static double GetViewportWidth(TextBlock textBlock)
    {
        for (var parent = VisualTreeHelper.GetParent(textBlock); parent != null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is FrameworkElement { ClipToBounds: true } clipped && clipped.ActualWidth > 0)
                return clipped.ActualWidth;
        }

        for (var parent = VisualTreeHelper.GetParent(textBlock); parent != null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is FrameworkElement element
                && element.ActualWidth > 0
                && element.ActualWidth < MeasureTextWidth(textBlock))
                return element.ActualWidth;
        }

        return textBlock.ActualWidth;
    }

    private static double MeasureTextWidth(TextBlock textBlock)
    {
        if (string.IsNullOrEmpty(textBlock.Text))
            return 0;

        var dpi = VisualTreeHelper.GetDpi(textBlock);
        var typeface = new Typeface(
            textBlock.FontFamily,
            textBlock.FontStyle,
            textBlock.FontWeight,
            textBlock.FontStretch);

        var formatted = new FormattedText(
            textBlock.Text,
            CultureInfo.CurrentUICulture,
            textBlock.FlowDirection,
            typeface,
            textBlock.FontSize,
            Brushes.Black,
            dpi.PixelsPerDip);

        return Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
    }
}

internal static class DependencyPropertyDescriptorHelper
{
    public static void AddValueChanged(DependencyObject source, DependencyProperty property, EventHandler handler)
    {
        var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(property, source.GetType());
        descriptor?.AddValueChanged(source, handler);
    }

    public static void RemoveValueChanged(DependencyObject source, DependencyProperty property, EventHandler handler)
    {
        var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(property, source.GetType());
        descriptor?.RemoveValueChanged(source, handler);
    }
}
