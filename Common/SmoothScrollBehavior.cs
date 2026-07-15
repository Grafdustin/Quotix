using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Quotix.Common;

/// <summary>
/// 全局平滑滚动。通过 <see cref="EventManager.RegisterClassHandler"/> 对 <see cref="ScrollViewer"/>
/// 类级注册滚轮路由事件，以缓动动画替代默认滚轮的整段跳动。无需在 XAML 中标记，所有
/// <see cref="ScrollViewer"/>（含 ListBox / DataGrid / Popup 内的列表）自动生效。
/// </summary>
public static class SmoothScrollBehavior
{
    /// <summary>动画时长（毫秒）。过小易显生硬，过大则连续滚动有拖影。</summary>
    private const double AnimationMs = 200;

    /// <summary>
    /// 动画目标垂直偏移的「影子」属性。因 <see cref="ScrollViewer.VerticalOffset"/> 只读、无法直接动画，
    /// 故动画此附加属性，并在其变化时调用 <see cref="ScrollViewer.ScrollToVerticalOffset"/> 驱动真实滚动。
    /// 像素滚动时单位为像素；按项滚动时单位为「项」（小数部分表示项内的部分偏移）。
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

        // 内容不足一屏时不拦截，让事件冒泡给外层可能的滚动容器
        if (sv.ScrollableHeight <= 0) return;

        e.Handled = true;

        double current = sv.VerticalOffset;
        double target;

        if (sv.CanContentScroll)
        {
            // 按项滚动（ListBox / DataGrid 等虚拟化容器）：偏移单位为「项」。
            // 将像素 delta 按行高换算成「项」数，再对分数项位置做缓动，
            // 既保留虚拟化语义，又实现像素级平滑滚动。
            double itemHeight = GetItemHeight(sv);
            if (itemHeight <= 1)
            {
                // 无法测量行高（暂无可见项）时退回默认行为，避免单位错乱跳变
                return;
            }
            double itemsToScroll = e.Delta / itemHeight; // 像素 → 项
            target = Math.Max(0, Math.Min(sv.ScrollableHeight, current - itemsToScroll));
        }
        else
        {
            // 像素滚动：偏移单位即像素
            target = Math.Max(0, Math.Min(sv.ScrollableHeight, current - e.Delta));
        }

        // 关键点：用 FillBehavior.HoldEnd + 显式 From=current，
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

    /// <summary>
    /// 测量按项滚动容器中单个项的高度（像素），用于将像素 delta 换算成「项」数。
    /// 通过可视化树找到第一个已实现的项容器（ListBoxItem / DataGridRow 等）取其 <see cref="FrameworkElement.ActualHeight"/>。
    /// </summary>
    private static double GetItemHeight(ScrollViewer sv)
    {
        if (sv.Content is not DependencyObject content) return 0;

        // 找到承载项的面板（VirtualizingStackPanel / DataGridRowsPresenter 等）
        var hostPanel = FindVisualChild<Panel>(content);
        if (hostPanel != null)
        {
            foreach (var child in hostPanel.Children)
            {
                if (child is FrameworkElement fe && fe.ActualHeight > 1)
                    return fe.ActualHeight;
            }
        }

        // 兜底：直接取内容里第一个有高度的视觉子元素
        var first = FindVisualChild<FrameworkElement>(content);
        return first?.ActualHeight ?? 0;
    }

    /// <summary>在可视化树中递归查找第一个指定类型的子元素。</summary>
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var descendant = FindVisualChild<T>(child);
            if (descendant != null) return descendant;
        }
        return null;
    }
}
