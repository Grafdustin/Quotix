using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Quotix.Common;

/// <summary>
/// 全局惯性滚动。列表类控件使用像素滚动，滚轮输入合并为连续的缓动动画。
/// </summary>
/// <remarks>
/// 全局关闭 <c>CanContentScroll</c>，避免 WPF-UI 控件模板继续按逻辑项滚动；
/// 同时将虚拟化面板的滚动单位设为像素。
/// 内层滚动到边界时不拦截滚轮，让事件继续传递给外层页面。
/// </remarks>
public static class SmoothScrollBehavior
{
    private const double PixelsPerWheelDelta = 0.85;
    private static readonly ConditionalWeakTable<ScrollViewer, InertialScrollState> States = new();

    /// <summary>在 <c>App.OnStartup</c> 调用一次，全局启用惯性滚动。</summary>
    public static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(ItemsControl),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnItemsControlLoaded));

        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnScrollViewerLoaded));

        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnMouseWheel));
    }

    private static void OnItemsControlLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ItemsControl ic)
            ApplyPixelScroll(ic);
    }

    private static void OnScrollViewerLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ScrollViewer viewer)
            return;

        viewer.PanningMode = PanningMode.VerticalFirst;
        viewer.PanningDeceleration = 0.001;
        _ = States.GetValue(viewer, static value => new InertialScrollState(value));
    }

    private static void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer viewer)
            return;

        var target = FindScrollableViewer(e.OriginalSource as DependencyObject, e.Delta);
        if (!ReferenceEquals(target, viewer))
            return;

        States.GetValue(viewer, static value => new InertialScrollState(value))
            .AddWheelDelta(e.Delta * PixelsPerWheelDelta);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollableViewer(DependencyObject? current, int delta)
    {
        while (current != null)
        {
            if (current is ScrollViewer viewer && CanScroll(viewer, delta))
                return viewer;

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private static bool CanScroll(ScrollViewer viewer, int delta)
    {
        if (viewer.ScrollableHeight <= 0)
            return false;

        return delta > 0
            ? viewer.VerticalOffset > 0
            : viewer.VerticalOffset < viewer.ScrollableHeight;
    }

    /// <summary>
    /// 将 <see cref="ItemsControl"/>（ListBox / DataGrid / ListView 等虚拟化容器）的
    /// <see cref="VirtualizingPanel.ScrollUnit"/> 设为 <see cref="VirtualizationScrollUnit.Pixel"/>，
    /// 使滚动按像素而非按项显示。仅设置一次（幂等）。
    /// </summary>
    private static void ApplyPixelScroll(ItemsControl ic)
    {
        if (VirtualizingPanel.GetScrollUnit(ic) != ScrollUnit.Pixel)
            VirtualizingPanel.SetScrollUnit(ic, ScrollUnit.Pixel);

        if (ScrollViewer.GetCanContentScroll(ic))
            ScrollViewer.SetCanContentScroll(ic, false);

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

    private sealed class InertialScrollState
    {
        private readonly ScrollViewer _viewer;
        private readonly DispatcherTimer _timer;
        private double _targetOffset;
        private DateTime _lastTick;

        public InertialScrollState(ScrollViewer viewer)
        {
            _viewer = viewer;
            _timer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(16),
                DispatcherPriority.Render,
                OnTick,
                viewer.Dispatcher);
            _timer.Stop();

            _viewer.Unloaded += (_, _) => Stop();
            _viewer.PreviewMouseLeftButtonDown += (_, _) => Stop();
            _viewer.PreviewTouchDown += (_, _) => Stop();
        }

        public void AddWheelDelta(double delta)
        {
            var baseline = _timer.IsEnabled ? _targetOffset : _viewer.VerticalOffset;
            _targetOffset = Math.Clamp(
                baseline - delta,
                0,
                _viewer.ScrollableHeight);

            if (_timer.IsEnabled)
                return;

            _lastTick = DateTime.UtcNow;
            _timer.Start();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (!_viewer.IsLoaded)
            {
                Stop();
                return;
            }

            var now = DateTime.UtcNow;
            var elapsed = Math.Clamp((now - _lastTick).TotalSeconds, 0.001, 0.05);
            _lastTick = now;

            var current = _viewer.VerticalOffset;
            var remaining = _targetOffset - current;
            if (Math.Abs(remaining) < 0.5)
            {
                _viewer.ScrollToVerticalOffset(_targetOffset);
                Stop();
                return;
            }

            var smoothing = 1 - Math.Exp(-14 * elapsed);
            _viewer.ScrollToVerticalOffset(current + (remaining * smoothing));
        }

        private void Stop()
        {
            _timer.Stop();
            _targetOffset = _viewer.VerticalOffset;
        }
    }
}
