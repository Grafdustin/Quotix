using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Quotix.ViewModels;

namespace Quotix.Views;

/// <summary>
/// 产品数据库视图，负责产品数据的展示、分页、列动态生成以及表切换功能。
/// </summary>
public partial class ProductDatabaseView : UserControl
{
    private ProductDatabaseViewModel? _currentVM;

    /// <summary>
    /// 初始化 ProductDatabaseView 实例。
    /// </summary>
    public ProductDatabaseView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// 视图加载时调用，重新订阅 ViewModel 事件并刷新数据。
    /// </summary>
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

    /// <summary>
    /// DataContext 变化时调用，处理 ViewModel 的绑定与解绑。
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachVM();

        if (DataContext is ProductDatabaseViewModel vm)
        {
            _currentVM = vm;
            vm.BeforeCollectionUpdate += OnBeforeCollectionUpdate;
            vm.AfterCollectionUpdate += OnAfterCollectionUpdate;
            RebuildColumns();

            ProductsGrid.UpdateLayout();
        }
    }

    /// <summary>
    /// 解绑当前 ViewModel 的事件订阅。
    /// </summary>
    private void DetachVM()
    {
        if (_currentVM == null) return;

        _currentVM.BeforeCollectionUpdate -= OnBeforeCollectionUpdate;
        _currentVM.AfterCollectionUpdate -= OnAfterCollectionUpdate;
        _currentVM = null;
    }

    /// <summary>
    /// 视图卸载时调用，清理事件订阅。
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachVM();
    }

    /// <summary>
    /// 集合即将更新时调用，解绑 DataGrid 的数据源。
    /// </summary>
    private void OnBeforeCollectionUpdate()
    {
    }

    /// <summary>
    /// 集合更新完毕后调用，重新构建列并绑定数据源。
    /// </summary>
    private void OnAfterCollectionUpdate()
    {
        if (_currentVM == null) return;

        RebuildColumns();
        ProductsGrid.UpdateLayout();
    }

    /// <summary>
    /// 根据 ViewModel 中的列头信息重新构建 DataGrid 列。
    /// </summary>
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

    /// <summary>
    /// 切换到 NDT 表。
    /// </summary>
    private void SwitchToNDT(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _currentVM?.SwitchToNDTCommand.Execute(null);
    }

    /// <summary>
    /// 切换到 RVI 表。
    /// </summary>
    private void SwitchToRVI(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _currentVM?.SwitchToRVICommand.Execute(null);
    }

    /// <summary>
    /// 分页大小选择变化时调用，更新页大小并重新加载数据。
    /// </summary>
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

    /// <summary>
    /// 分页大小下拉框加载时调用，添加弹出动画效果。
    /// </summary>
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
                    // 检测 Popup 是向上还是向下展开
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

    /// <summary>
    /// 在可视化树中查找指定类型的子元素。
    /// </summary>
    /// <typeparam name="T">要查找的元素类型</typeparam>
    /// <param name="parent">父元素</param>
    /// <returns>找到的第一个匹配元素，未找到则返回 null</returns>
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
