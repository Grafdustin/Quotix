using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Quotix.Common;

/// <summary>
/// 全局平滑滚动。通过 <see cref="EventManager.RegisterClassHandler"/> 对 <see cref="ScrollViewer"/>
/// 类级注册滚轮路由事件，接管像素滚动（<c>CanContentScroll=False</c>）的滚轮行为，
/// 以缓动动画替代默认的整段跳动。无需在 XAML 中标记，所有 ScrollViewer 自动生效。
/// </summary>
/// <remarks>
/// 仅对像素级滚动接管；对按项滚动的虚拟化容器
/// （ListBox / DataGrid 默认 <c>CanContentScroll=True</c>，其 VerticalOffset 单位是「项」而非像素）
/// 不拦截，保持默认行为以兼顾性能与滚动步长语义。
/// </remarks>
public static class SmoothScrollBehavior
{
    /// <summary>动画时长（毫秒）。</summary>
    private const double AnimationMs = 260;

    /// <summary>当前动画正在逼近的目标偏移，用于连续滚动时叠加而非从头再来。</summary>
    private static readonly DependencyProperty PendingOffsetProperty =
        DependencyProperty.RegisterAttached(
            "PendingOffset", typeof(double), typeof(SmoothScrollBehavior),
            new PropertyMetadata(0.0));

    /// <summary>标记该 ScrollViewer 是否有平滑滚动动画进行中。</summary>
    private static readonly DependencyProperty IsAnimatingProperty =
        DependencyProperty.RegisterAttached(
            "IsAnimating", typeof(bool), typeof(SmoothScrollBehavior),
            new PropertyMetadata(false));

    /// <summary>
    /// 动画目标垂直偏移的「影子」属性。因 <see cref="ScrollViewer.VerticalOffset"/> 只读、无法直接动画，
    /// 故动画此附加属性，并在其变化时调用 <see cref="ScrollViewer.ScrollToVerticalOffset"/> 驱动真实滚动。
    /// </summary>
    private static readonly DependencyProperty ShadowOffsetProperty =
        DependencyProperty.RegisterAttached(
            "ShadowOffset", typeof(double), typeof(SmoothScrollBehavior),
            new PropertyMetadata(0.0, OnShadowOffsetChanged));

    /// <summary>在 <c>App.OnStartup</c> 调用一次，全局启用平滑滚动。</summary>
    public static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            ScrollViewer.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheel));
    }

    private static void OnShadowOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer sv)
            sv.ScrollToVerticalOffset((double)e.NewValue);
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;

        // 按项滚动的虚拟化容器不接管（偏移单位是项，非像素），保持默认行为
        if (sv.CanContentScroll) return;
        // 内容不足以滚动时不拦截，让事件冒泡给可能的父级滚动容器
        if (sv.ScrollableHeight <= 0) return;

        e.Handled = true;

        double current = sv.VerticalOffset;
        // 连续快速滚动时从上一次目标继续叠加，避免动画被打断产生回弹
        bool animating = (bool)sv.GetValue(IsAnimatingProperty);
        double baseline = animating ? (double)sv.GetValue(PendingOffsetProperty) : current;

        double target = baseline - e.Delta; // 一格 delta=120，直接作为像素步长
        target = Math.Max(0, Math.Min(sv.ScrollableHeight, target));

        sv.SetValue(PendingOffsetProperty, target);
        sv.SetValue(IsAnimatingProperty, true);

        var anim = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(AnimationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        anim.Completed += (_, _) => sv.SetValue(IsAnimatingProperty, false);

        // 从当前实际偏移起步，动画到目标；先清旧动画避免叠加冲突
        sv.BeginAnimation(ShadowOffsetProperty, null);
        sv.SetValue(ShadowOffsetProperty, current);
        sv.BeginAnimation(ShadowOffsetProperty, anim);
    }
}
