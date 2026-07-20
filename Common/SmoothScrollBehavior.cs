using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Quotix.Common;

/// <summary>
/// 全局平滑滚动。通过 <see cref="EventManager.RegisterClassHandler"/> 对 <see cref="ScrollViewer"/>
/// 类级注册滚轮路由事件，以缓动动画替代默认滚轮的整段跳动。无需在 XAML 中标记，所有
/// <see cref="ScrollViewer"/>（含 ListBox / DataGrid / Popup 内的列表）自动生效。
/// </summary>
/// <remarks>
/// 关键：虚拟化容器（ListBox / DataGrid 默认 <c>CanContentScroll=True</c>，按「项」滚动）在
/// 按项模式下会把分数 <see cref="ScrollViewer.VerticalOffset"/> 取整到整数项，导致动画仍一格一格跳。
/// 因此对每个承载 <see cref="ItemsControl"/> 的滚动容器，将其
/// <see cref="VirtualizingPanel.ScrollUnit"/> 设为 <see cref="VirtualizationScrollUnit.Pixel"/>，
/// 既切换为像素滚动（动画连贯）又保留虚拟化（仅实例化可见项，大数据量不卡）。
/// </remarks>
public static class SmoothScrollBehavior
{
    /// <summary>动画时长（毫秒）。过小易显生硬，过大则连续滚动有拖影。</summary>
    private const double AnimationMs = 200;

    /// <summary>
    /// 动画目标垂直偏移的「影子」属性。因 <see cref="ScrollViewer.VerticalOffset"/> 只读、无法直接动画，
    /// 故动画此附加属性，并在其变化时调用 <see cref="ScrollViewer.ScrollToVerticalOffset"/> 驱动真实滚动。
    /// 设为像素滚动后单位即像素。
    /// </summary>
    private static readonly DependencyProperty ShadowOffsetProperty =
        DependencyProperty.RegisterAttached(
            "ShadowOffset", typeof(double), typeof(SmoothScrollBehavior),
            new PropertyMetadata(0.0, OnShadowOffsetChanged));

    /// <summary>
    /// 最近一次滚轮输入累计出的目标偏移。连续滚动时若只按动画中的当前位置计算，
    /// 高频滚轮输入会反复落在相近目标上，表现为后续滚动不生效。
    /// </summary>
    private static readonly DependencyProperty TargetOffsetProperty =
        DependencyProperty.RegisterAttached(
            "TargetOffset", typeof(double), typeof(SmoothScrollBehavior),
            new PropertyMetadata(double.NaN));

    /// <summary>当前滚动动画代号，用于避免旧动画完成回调清理新动画。</summary>
    private static readonly DependencyProperty AnimationGenerationProperty =
        DependencyProperty.RegisterAttached(
            "AnimationGeneration", typeof(int), typeof(SmoothScrollBehavior),
            new PropertyMetadata(0));

    /// <summary>在 <c>App.OnStartup</c> 调用一次，全局启用平滑滚动。</summary>
    public static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(ItemsControl),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnItemsControlLoaded));

        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            ScrollViewer.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheel),
            true);
    }

    private static void OnShadowOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer sv)
            sv.ScrollToVerticalOffset((double)e.NewValue);
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // 类级注册保证 sender 即 ScrollViewer；若不是则放行
        if (sender is not ScrollViewer sv) return;

        // 内容不足一屏时不拦截，让事件冒泡给外层可能的滚动容器
        if (sv.ScrollableHeight <= 0) return;

        // 确保虚拟化容器按像素滚动（动画连贯且保留虚拟化）
        EnsurePixelScroll(sv);

        double current = sv.VerticalOffset;
        double previousTarget = (double)sv.GetValue(TargetOffsetProperty);
        if (double.IsNaN(previousTarget) ||
            previousTarget < 0 ||
            previousTarget > sv.ScrollableHeight)
        {
            previousTarget = current;
        }

        double target = Clamp(previousTarget - e.Delta, 0, sv.ScrollableHeight);

        // 已在当前 ScrollViewer 边界时放行，避免内层滚动容器吞掉外层页面的滚轮。
        if (Math.Abs(target - current) < 0.1 &&
            ((e.Delta > 0 && current <= 0) ||
             (e.Delta < 0 && current >= sv.ScrollableHeight)))
        {
            sv.SetValue(TargetOffsetProperty, current);
            return;
        }

        e.Handled = true;
        sv.SetValue(TargetOffsetProperty, target);

        // 关键点：目标按 TargetOffset 累加，动画从当前有效位置接管。
        int generation = (int)sv.GetValue(AnimationGenerationProperty) + 1;
        sv.SetValue(AnimationGenerationProperty, generation);
        var anim = new DoubleAnimation
        {
            From = current,
            To = target,
            Duration = TimeSpan.FromMilliseconds(AnimationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        anim.Completed += (_, _) =>
        {
            if ((int)sv.GetValue(AnimationGenerationProperty) != generation)
                return;

            sv.BeginAnimation(ShadowOffsetProperty, null);
            sv.SetValue(ShadowOffsetProperty, target);
            sv.SetValue(TargetOffsetProperty, target);
        };

        sv.BeginAnimation(ShadowOffsetProperty, anim);
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static void OnItemsControlLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ItemsControl ic)
            ApplyPixelScroll(ic);
    }

    /// <summary>
    /// 若该滚动容器承载的是 <see cref="ItemsControl"/>（ListBox / DataGrid / ListView 等虚拟化容器），
    /// 则将其 <see cref="VirtualizingPanel.ScrollUnit"/> 设为 <see cref="VirtualizationScrollUnit.Pixel"/>，
    /// 使滚动按像素而非按项，从而让缓动动画连贯显示。仅设置一次（幂等）。
    /// </summary>
    private static void EnsurePixelScroll(ScrollViewer sv)
    {
        ItemsControl? ic = sv.TemplatedParent as ItemsControl;
        if (ic == null)
        {
            // 视觉树向上回溯查找 ItemsControl 宿主（自定义模板时可落到此分支）
            DependencyObject? parent = VisualTreeHelper.GetParent(sv);
            while (parent != null && ic == null)
            {
                if (parent is ItemsControl found) ic = found;
                parent = VisualTreeHelper.GetParent(parent);
            }
        }

        if (ic != null)
            ApplyPixelScroll(ic);
    }

    private static void ApplyPixelScroll(ItemsControl ic)
    {
        if (VirtualizingPanel.GetScrollUnit(ic) != ScrollUnit.Pixel)
            VirtualizingPanel.SetScrollUnit(ic, ScrollUnit.Pixel);

        if (!ScrollViewer.GetCanContentScroll(ic))
            ScrollViewer.SetCanContentScroll(ic, true);

        ApplyPixelScrollToVirtualizingPanels(ic);
    }

    private static void ApplyPixelScrollToVirtualizingPanels(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is VirtualizingPanel panel &&
                VirtualizingPanel.GetScrollUnit(panel) != ScrollUnit.Pixel)
            {
                VirtualizingPanel.SetScrollUnit(panel, ScrollUnit.Pixel);
            }

            ApplyPixelScrollToVirtualizingPanels(child);
        }
    }
}
