using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Quotix.Common;

/// <summary>
/// TextBlock 超出容器宽度时横向轮播显示完整文本。
/// </summary>
public static class MarqueeTextBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(MarqueeTextBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

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

            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var textWidth = textBlock.DesiredSize.Width;
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

            transform.BeginAnimation(TranslateTransform.XProperty, null);

            var duration = TimeSpan.FromSeconds(Math.Clamp(overflow / 24, 3, 8));
            var animation = new DoubleAnimation
            {
                From = 0,
                To = -overflow,
                BeginTime = TimeSpan.FromSeconds(0.8),
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
                && element.ActualWidth < textBlock.DesiredSize.Width)
                return element.ActualWidth;
        }

        return textBlock.ActualWidth;
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
