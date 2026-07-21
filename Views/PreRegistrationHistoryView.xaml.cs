using System.Windows;
using System.Windows.Controls;
using Quotix.ViewModels;

namespace Quotix.Views;

/// <summary>
/// NDT 预报备记录页。
/// </summary>
public partial class PreRegistrationHistoryView : UserControl
{
    private PreRegistrationHistoryViewModel VM => (PreRegistrationHistoryViewModel)DataContext;

    public PreRegistrationHistoryView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await VM.RefreshAsync();
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string id)
            VM.ExportCommand.Execute(id);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string id)
            VM.DeleteCommand.Execute(id);
    }
}
