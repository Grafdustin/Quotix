using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Quotix.Common;

/// <summary>
/// 全局平滑滚动。通过 <see cref="EventManager.RegisterClassHandler"/> 对 <see cref="ScrollViewer"/>
/// 类级注册滚轮路由事件，以缓动动画替代默认滚轮的整段跳动。无需在 XAML 中标记，所有
/// <see cref="ScrollViewer"/> 自动生效。
/// </summary>
/// <remarks>
/// 仅接管像素级滚动（<c>CanContentScroll=False</c>）。按项滚动的虚拟化容器
/// （<c>CanContentScroll=True</c> 时 <see cref="ScrollViewer.VerticalOffset"/> 单位是「项」而非像素，
/// 直接减去像素 delta 会导致跳变）不拦截，保持默认行为。
/// </remarks>
public static class SmoothScrollBehavior
{
    /// <summary>动画时长（毫秒）。过小易显生硬，过大则连续滚动有拖影。</summary>
    private const double AnimationMs = 200;

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
        // 类级注册保证 sender 即 ScrollViewer；若不是则放行
        if (sender is not ScrollViewer sv) return;

        // 按项滚动的虚拟化容器（偏移单位为「项」）不接管，避免单位错乱导致跳变
        if (sv.CanContentScroll) return;

        // 内容不足一屏时不拦截，让事件冒泡给外层可能的滚动容器
        if (sv.ScrollableHeight <= 0) return;

        e.Handled = true;

        double current = sv.VerticalOffset;
        // delta 为负表示向上滚动（内容应上移），故偏移减少；取反后向上为正向
        double target = Math.Max(0, Math.Min(sv.ScrollableHeight, current - e.Delta));

        // 关键点：不调用 BeginAnimation(null) 停止旧动画。
        // 旧实现用 FillBehavior.Stop，动画结束会把 ShadowOffset 回退到本地值（原点），
        // 造成「滚上去又弹回顶部」的卡顿与「看起来没生效」的假象。
        // 这里改用 FillBehavior.HoldEnd 并显式指定 From=current：
        // 新动画自动从当前有效值接管，连续快速滚动时平滑衔接、无回弹、无跳变。
        var anim = new DoubleAnimation
        {
            From = current,
            To = target,
            Duration = TimeSpan.FromMilliseconds(AnimationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        sv.BeginAnimation(ShadowOffsetProperty, anim);
    }
}
