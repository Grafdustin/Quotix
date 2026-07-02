using System;
using System.Windows;
using System.Windows.Controls;
using Quotix.Models;
using Quotix.ViewModels;

namespace Quotix.Views;

/// <summary>
/// 历史报价单视图，负责历史报价单的展示、编辑、导出和删除功能。
/// </summary>
public partial class HistoryView : UserControl
{
    /// <summary>
    /// 获取当前数据上下文作为 HistoryViewModel。
    /// </summary>
    private HistoryViewModel VM => (HistoryViewModel)DataContext;

    /// <summary>
    /// 初始化 HistoryView 实例。
    /// </summary>
    public HistoryView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await VM.RefreshAsync();
    }

    /// <summary>
    /// 编辑按钮点击事件，触发编辑命令。
    /// </summary>
    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string id)
            VM.EditCommand.Execute(id);
    }

    /// <summary>
    /// 导出按钮点击事件，触发导出命令。
    /// </summary>
    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string id)
            VM.ExportCommand.Execute(id);
    }

    /// <summary>
    /// 删除按钮点击事件，触发删除命令。
    /// </summary>
    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string id)
            VM.DeleteCommand.Execute(id);
    }

    /// <summary>
    /// 搜索框文本变化时调用，对报价单列表进行前端过滤。
    /// </summary>
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // 简单前端过滤：直接操作 CollectionView
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(VM.Quotations);
        if (view == null) return;

        var filterText = ((TextBox)sender).Text?.Trim() ?? "";
        view.Filter = string.IsNullOrEmpty(filterText)
            ? null
            : item => item is Quotation q &&
                (q.CustomerName?.Contains(filterText, StringComparison.OrdinalIgnoreCase) == true ||
                 q.Filename?.Contains(filterText, StringComparison.OrdinalIgnoreCase) == true ||
                 q.CompanyContact?.Contains(filterText, StringComparison.OrdinalIgnoreCase) == true);
    }
}
