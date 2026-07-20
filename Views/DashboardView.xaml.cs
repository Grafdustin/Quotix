using System.Windows;
using System.Windows.Controls;
using Quotix.ViewModels;

namespace Quotix.Views;

/// <summary>
/// 首页仪表盘视图。
/// </summary>
public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
            await vm.RefreshAsync();
    }
}
