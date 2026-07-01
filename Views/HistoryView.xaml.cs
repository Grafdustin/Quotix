using System;
using System.Windows;
using System.Windows.Controls;
using Quotix.Models;
using Quotix.ViewModels;

namespace Quotix.Views;

public partial class HistoryView : UserControl
{
    private HistoryViewModel VM => (HistoryViewModel)DataContext;

    public HistoryView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await VM.RefreshAsync();
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string id)
            VM.EditCommand.Execute(id);
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string id)
            VM.ExportCommand.Execute(id);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string id)
            VM.DeleteCommand.Execute(id);
    }

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
