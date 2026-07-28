using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Quotix.Common;

/// <summary>
/// 全局滚动优化。保留 WPF 原生滚轮处理，仅将列表类控件切换为像素滚动。
/// </summary>
/// <remarks>
/// 关键：虚拟化容器（ListBox / DataGrid 默认 <c>CanContentScroll=True</c>，按「项」滚动）在
/// 按项模式下滚轮会一格一格跳。对每个 <see cref="ItemsControl"/> 将其
/// <see cref="VirtualizingPanel.ScrollUnit"/> 设为 <see cref="VirtualizationScrollUnit.Pixel"/>，
/// 既切换为像素滚动又保留虚拟化（仅实例化可见项，大数据量不卡）。
/// </remarks>
public static class SmoothScrollBehavior
{
    /// <summary>在 <c>App.OnStartup</c> 调用一次，全局启用滚动优化。</summary>
    public static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(ItemsControl),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnItemsControlLoaded));
    }

    private static void OnItemsControlLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ItemsControl ic)
            ApplyPixelScroll(ic);
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
