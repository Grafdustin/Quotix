using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Quotix.Common;

/// <summary>
/// 为全局滚动区域提供保留虚拟化的像素滚动和短时惯性动画。
/// </summary>
public static class SmoothScrollBehavior
{
    private const double PixelsPerWheelNotch = 48;
    private const double AnimationDurationMilliseconds = 150;
    private const double BoundaryTolerance = 0.5;

    private static readonly Dictionary<ScrollViewer, ScrollAnimation> Animations = [];
    private static bool _isRegistered;
    private static bool _isRendering;

    /// <summary>在 <c>App.OnStartup</c> 调用一次，全局启用滚动优化。</summary>
    public static void Register()
    {
        if (_isRegistered)
            return;

        _isRegistered = true;

        EventManager.RegisterClassHandler(
            typeof(ItemsControl),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnItemsControlLoaded));

        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnScrollViewerLoaded));

        // 使用冒泡事件，让最内层 ScrollViewer 优先接管；滚动到边界后事件会继续交给外层。
        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheel),
            true);

        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnPointerDown));

        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewTouchDownEvent,
            new EventHandler<TouchEventArgs>(OnTouchDown));

        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(OnKeyDown));
    }

    private static void OnItemsControlLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ItemsControl itemsControl)
            return;

        ApplyPixelScroll(itemsControl);

        // 控件模板中的 ScrollViewer/虚拟化面板可能在 Loaded 之后才生成。
        itemsControl.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () => ApplyPixelScroll(itemsControl));
    }

    private static void OnScrollViewerLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
            ScrollViewer.SetCanContentScroll(scrollViewer, false);
    }

    private static void ApplyPixelScroll(ItemsControl itemsControl)
    {
        VirtualizingPanel.SetScrollUnit(itemsControl, ScrollUnit.Pixel);
        ScrollViewer.SetCanContentScroll(itemsControl, false);
        ApplyPixelScrollToVisualTree(itemsControl);
    }

    private static void ApplyPixelScrollToVisualTree(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is VirtualizingPanel panel)
                VirtualizingPanel.SetScrollUnit(panel, ScrollUnit.Pixel);

            if (child is ScrollViewer scrollViewer)
                ScrollViewer.SetCanContentScroll(scrollViewer, false);

            ApplyPixelScrollToVisualTree(child);
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer || e.Delta == 0)
            return;

        ScrollViewer? target = FindScrollableViewer(e.OriginalSource as DependencyObject, e.Delta);
        if (!ReferenceEquals(scrollViewer, target))
            return;

        double currentTarget = GetTargetOffset(scrollViewer);

        double wheelNotches = e.Delta / 120d;
        double requestedTarget = currentTarget - (wheelNotches * PixelsPerWheelNotch);
        double targetOffset = Math.Clamp(requestedTarget, 0, scrollViewer.ScrollableHeight);

        Animations[scrollViewer] = new ScrollAnimation(
            scrollViewer.VerticalOffset,
            targetOffset,
            Stopwatch.GetTimestamp());

        StartRendering();
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollableViewer(DependencyObject? source, int wheelDelta)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer &&
                CanScroll(scrollViewer, wheelDelta))
            {
                return scrollViewer;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static bool CanScroll(ScrollViewer scrollViewer, int wheelDelta)
    {
        if (scrollViewer.ScrollableHeight <= BoundaryTolerance)
            return false;

        double targetOffset = GetTargetOffset(scrollViewer);
        return wheelDelta < 0
            ? targetOffset < scrollViewer.ScrollableHeight - BoundaryTolerance
            : targetOffset > BoundaryTolerance;
    }

    private static double GetTargetOffset(ScrollViewer scrollViewer)
    {
        return Animations.TryGetValue(scrollViewer, out ScrollAnimation? animation)
            ? animation.TargetOffset
            : scrollViewer.VerticalOffset;
    }

    private static DependencyObject? GetParent(DependencyObject child)
    {
        if (child is Visual or Visual3D)
            return VisualTreeHelper.GetParent(child);

        if (child is ContentElement contentElement)
        {
            DependencyObject? parent = ContentOperations.GetParent(contentElement);
            if (parent is not null)
                return parent;

            if (contentElement is FrameworkContentElement frameworkContentElement)
                return frameworkContentElement.Parent;
        }

        return LogicalTreeHelper.GetParent(child);
    }

    private static void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
            CancelAnimation(scrollViewer);
    }

    private static void OnTouchDown(object? sender, TouchEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
            CancelAnimation(scrollViewer);
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
            CancelAnimation(scrollViewer);
    }

    private static void StartRendering()
    {
        if (_isRendering)
            return;

        _isRendering = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        if (Animations.Count == 0)
        {
            StopRendering();
            return;
        }

        long now = Stopwatch.GetTimestamp();
        List<ScrollViewer>? completed = null;

        foreach ((ScrollViewer scrollViewer, ScrollAnimation animation) in Animations)
        {
            if (!scrollViewer.IsLoaded)
            {
                (completed ??= []).Add(scrollViewer);
                continue;
            }

            double elapsedMilliseconds =
                (now - animation.StartTimestamp) * 1000d / Stopwatch.Frequency;
            double progress = Math.Clamp(
                elapsedMilliseconds / AnimationDurationMilliseconds,
                0,
                1);
            double easedProgress = 1 - Math.Pow(1 - progress, 3);
            double offset = animation.StartOffset +
                            ((animation.TargetOffset - animation.StartOffset) * easedProgress);

            scrollViewer.ScrollToVerticalOffset(
                Math.Clamp(offset, 0, scrollViewer.ScrollableHeight));

            if (progress >= 1)
                (completed ??= []).Add(scrollViewer);
        }

        if (completed is null)
            return;

        foreach (ScrollViewer scrollViewer in completed)
            Animations.Remove(scrollViewer);
    }

    private static void CancelAnimation(ScrollViewer scrollViewer)
    {
        Animations.Remove(scrollViewer);
        if (Animations.Count == 0)
            StopRendering();
    }

    private static void StopRendering()
    {
        if (!_isRendering)
            return;

        CompositionTarget.Rendering -= OnRendering;
        _isRendering = false;
    }

    private sealed record ScrollAnimation(
        double StartOffset,
        double TargetOffset,
        long StartTimestamp);
}
