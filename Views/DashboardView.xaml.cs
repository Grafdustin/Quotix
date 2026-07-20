using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private void ChartCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not DashboardViewModel vm || sender is not FrameworkElement chart)
            return;

        var position = e.GetPosition(chart);
        var scaleX = chart.ActualWidth > 0 ? 640 / chart.ActualWidth : 1;
        var scaleY = chart.ActualHeight > 0 ? 180 / chart.ActualHeight : 1;
        vm.ShowChartDetail(position.X * scaleX, position.Y * scaleY);
    }

    private void ChartCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
            vm.HideChartDetail();
    }
}
