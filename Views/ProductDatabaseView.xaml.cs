using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Quotix.ViewModels;

namespace Quotix.Views;

public partial class ProductDatabaseView : UserControl
{
    private ProductDatabaseViewModel? _currentVM;

    public ProductDatabaseView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 标签页切回时，View 被重新挂载但 DataContext 未变，需重新订阅 VM 事件
        if (_currentVM == null && DataContext is ProductDatabaseViewModel vm)
        {
            _currentVM = vm;
            vm.BeforeCollectionUpdate += OnBeforeCollectionUpdate;
            vm.AfterCollectionUpdate += OnAfterCollectionUpdate;
        }

        // 首次加载时如果数据为空则触发刷新
        if (_currentVM != null && _currentVM.TotalCount == 0)
            _currentVM.Refresh();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachVM();

        if (DataContext is ProductDatabaseViewModel vm)
        {
            _currentVM = vm;
            vm.BeforeCollectionUpdate += OnBeforeCollectionUpdate;
            vm.AfterCollectionUpdate += OnAfterCollectionUpdate;
            RebuildColumns();

            // 切换标签页时会重建 View，已有数据的 VM 需要恢复 ItemsSource
            if (vm.Products.Count > 0)
                ProductsGrid.ItemsSource = vm.Products;

            ProductsGrid.UpdateLayout();
        }
    }

    private void DetachVM()
    {
        if (_currentVM == null) return;

        _currentVM.BeforeCollectionUpdate -= OnBeforeCollectionUpdate;
        _currentVM.AfterCollectionUpdate -= OnAfterCollectionUpdate;
        _currentVM = null;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachVM();
    }

    private void OnBeforeCollectionUpdate()
    {
        ProductsGrid.ItemsSource = null;
    }

    private void OnAfterCollectionUpdate()
    {
        if (_currentVM == null) return;

        RebuildColumns();
        ProductsGrid.ItemsSource = _currentVM.Products;
        ProductsGrid.UpdateLayout();
    }

    private void RebuildColumns()
    {
        if (_currentVM == null) return;

        ProductsGrid.Columns.Clear();

        foreach (var header in _currentVM.ColumnHeaders)
        {
            ProductsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 80,
                Binding = new System.Windows.Data.Binding($"Data[{header}]")
            });
        }

        if (_currentVM.ColumnHeaders.Count == 0)
        {
            ProductsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "数据",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new System.Windows.Data.Binding("Data")
            });
        }
    }

    private void SwitchToNDT(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _currentVM?.SwitchToNDTCommand.Execute(null);
    }

    private void SwitchToRVI(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _currentVM?.SwitchToRVICommand.Execute(null);
    }

    private void PageSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_currentVM == null || sender is not ComboBox cb || cb.SelectedItem is not ComboBoxItem item)
            return;

        if (int.TryParse(item.Tag?.ToString(), out var size) && size != _currentVM.PageSize)
        {
            _currentVM.PageSize = size;
            _ = _currentVM.LoadPageAsync(1);
        }
    }

    private void PageSizeCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox cb) return;

        cb.ApplyTemplate();
        if (cb.Template.FindName("PART_Popup", cb) is Popup popup)
        {
            popup.Opened += (s, ev) =>
            {
                if (popup.Child is not FrameworkElement popupRoot) return;

                // 用 Dispatcher 延迟确保视觉树完全加载
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 检测Popup是向上还是向下展开
                    var transform = popupRoot.TransformToAncestor(cb);
                    var relativePos = transform.Transform(new Point(0, 0));
                    bool isOpeningUpward = relativePos.Y < 0;

                    // 对 Popup 根元素整体做缩放动画
                    // 向上展开: 原点在底部 → ScaleY 0→1 从下向上滑出
                    // 向下展开: 原点在顶部 → ScaleY 0→1 从上向下滑出
                    popupRoot.RenderTransformOrigin = isOpeningUpward
                        ? new Point(0, 1)
                        : new Point(0, 0);

                    var scale = new ScaleTransform(1, 0);
                    popupRoot.RenderTransform = scale;

                    var anim = new DoubleAnimation
                    {
                        From = 0.0,
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(180),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };

                    // 冻结动画以提高性能
                    anim.Freeze();

                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                }), System.Windows.Threading.DispatcherPriority.Render);
            };

            popup.Closed += (s, ev) =>
            {
                if (popup.Child is FrameworkElement popupRoot)
                {
                    popupRoot.RenderTransform = null;
                }
            };
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
                return result;
            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
                return descendant;
        }
        return null;
    }
}
