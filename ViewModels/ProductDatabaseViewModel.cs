using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Quotix.Models;
using Quotix.Services;

namespace Quotix.ViewModels;

/// <summary>
/// 产品数据库视图模型，负责产品数据的分页查询、导入、删除以及表切换功能。
/// </summary>
public partial class ProductDatabaseViewModel : ObservableObject
{
    private readonly ProductService _productService;
    private readonly ProductImportService _importService;
    private readonly DialogService _dialog;

    /// <summary>
    /// 初始化 ProductDatabaseViewModel 实例。
    /// </summary>
    /// <param name="productService">产品服务</param>
    /// <param name="importService">产品导入服务</param>
    /// <param name="dialog">对话框服务</param>
    public ProductDatabaseViewModel(ProductService productService, ProductImportService importService, DialogService dialog)
    {
        _productService = productService;
        _importService = importService;
        _dialog = dialog;
    }

    private int _pendingSearchToken;
    /// <summary>加载请求序号，用于丢弃过期加载结果（并发调用时仅最新一次生效）</summary>
    private int _loadToken;

    /// <summary>
    /// 产品数据行集合。
    /// </summary>
    public ObservableCollection<ProductRowViewModel> Products { get; } = new();

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _statusText = "就绪";
    /// <summary>
    /// 是否为 NDT 表（否则为 RVI 表）。
    /// </summary>
    [ObservableProperty] private bool _isNDT = true;
    /// <summary>
    /// 是否为主表（否则为副表）。
    /// </summary>
    [ObservableProperty] private bool _isMainTable = true;
    [ObservableProperty] private string _currentTableLabel = "NDT - 价表";
    [ObservableProperty] private bool _isLoading;

    // —— 分页 ——
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _totalPages;
    [ObservableProperty] private int _pageSize = 50;

    private int _currentPage = 1;
    /// <summary>
    /// 当前页码。
    /// </summary>
    public int CurrentPage
    {
        get => _currentPage;
        set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(CanGoPrev));
                OnPropertyChanged(nameof(CanGoNext));
                NextPageCommand.NotifyCanExecuteChanged();
                PrevPageCommand.NotifyCanExecuteChanged();
                GoToFirstPageCommand.NotifyCanExecuteChanged();
                GoToLastPageCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// 总页数理变化时，通知分页命令状态更新。
    /// </summary>
    partial void OnTotalPagesChanged(int value)
    {
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
        NextPageCommand.NotifyCanExecuteChanged();
        PrevPageCommand.NotifyCanExecuteChanged();
        GoToFirstPageCommand.NotifyCanExecuteChanged();
        GoToLastPageCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 是否可以向前翻页。
    /// </summary>
    public bool CanGoPrev => CurrentPage > 1;
    /// <summary>
    /// 是否可以向后翻页。
    /// </summary>
    public bool CanGoNext => CurrentPage < TotalPages;

    /// <summary>
    /// 列头集合。
    /// </summary>
    public ObservableCollection<string> ColumnHeaders { get; } = new();

    /// <summary>
    /// View 订阅：集合即将更新，请解绑 DataGrid。
    /// </summary>
    public event Action? BeforeCollectionUpdate;
    /// <summary>
    /// View 订阅：集合更新完毕，请重新绑定 DataGrid。
    /// </summary>
    public event Action? AfterCollectionUpdate;

    // —— 搜索文本变化时 300ms 防抖重新加载 ——
    partial void OnSearchTextChanged(string value)
    {
        var token = ++_pendingSearchToken;
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            if (token == _pendingSearchToken)
                _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () => await LoadPageAsync(1));
        });
    }

    /// <summary>
    /// 获取当前选中的表名。
    /// </summary>
    /// <returns>表名字符串</returns>
    private string GetCurrentTableName()
    {
        if (IsNDT)
            return IsMainTable ? "products_ndt" : "products_ndt_delivery";
        else
            return IsMainTable ? "products_rvi_change" : "products_rvi_ot";
    }

    /// <summary>
    /// 刷新当前页数据。
    /// </summary>
    public void Refresh()
    {
        _ = LoadPageAsync(1);
    }

    /// <summary>
    /// 异步加载指定页的数据。
    /// </summary>
    /// <param name="page">目标页码</param>
    public async Task LoadPageAsync(int page)
    {
        IsLoading = true;
        StatusText = "加载中...";
        var loadToken = ++_loadToken;

        var tableName = GetCurrentTableName();
        var keyword = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        var localPage = page;
        var localPageSize = PageSize;

        CurrentTableLabel = IsNDT
            ? (IsMainTable ? "NDT - 价表" : "NDT - 货期")
            : (IsMainTable ? "RVI - Change" : "RVI - OT Code");

        // 后台线程：DB 分页查询 + JSON 解析
        var (rows, columnHeaders, totalCount) = await Task.Run(() =>
        {
            var (products, total) = _productService.GetProductsPaged(tableName, keyword, localPage, localPageSize);

            var allKeys = new HashSet<string>();
            var rowVms = new List<ProductRowViewModel>(products.Count);

            foreach (var p in products)
            {
                var row = new ProductRowViewModel { Id = p.Id, CreatedBy = p.CreatedBy };

                try
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(p.DataJson);
                    if (dict != null)
                    {
                        foreach (var kv in dict)
                        {
                            var val = kv.Value ?? "";
                            row.Data[kv.Key] = val;
                            allKeys.Add(kv.Key);
                        }
                    }
                }
                catch { }

                rowVms.Add(row);
            }

            return (rowVms, allKeys.ToList(), total);
        });

        // 若已有更新的加载请求发起，丢弃本次结果，避免旧数据覆盖新数据
        if (loadToken != _loadToken)
            return;

        // ---- UI 线程：批量更新 ----
        BeforeCollectionUpdate?.Invoke();

        CurrentPage = localPage;
        TotalCount = totalCount;
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / localPageSize));

        ColumnHeaders.Clear();
        foreach (var h in columnHeaders)
            ColumnHeaders.Add(h);

        Products.Clear();
        foreach (var row in rows)
            Products.Add(row);

        AfterCollectionUpdate?.Invoke();

        StatusText = keyword != null
            ? $"搜索 [{keyword}] - 共 {TotalCount:N0} 条记录，第 {CurrentPage}/{TotalPages} 页"
            : $"共 {TotalCount:N0} 条记录，第 {CurrentPage}/{TotalPages} 页";
        IsLoading = false;
    }

    // —— 分页导航 ——

    /// <summary>
    /// 跳转到下一页。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextPage()
    {
        if (CurrentPage < TotalPages)
            await LoadPageAsync(CurrentPage + 1);
    }

    /// <summary>
    /// 跳转到上一页。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoPrev))]
    private async Task PrevPage()
    {
        if (CurrentPage > 1)
            await LoadPageAsync(CurrentPage - 1);
    }

    /// <summary>
    /// 跳转到第一页。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoPrev))]
    private async Task GoToFirstPage() => await LoadPageAsync(1);

    /// <summary>
    /// 跳转到最后一页。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task GoToLastPage() => await LoadPageAsync(TotalPages);

    // —— 表切换 ——

    /// <summary>
    /// 切换到 NDT 表。
    /// </summary>
    [RelayCommand]
    private async Task SwitchToNDT()
    {
        IsNDT = true;
        IsMainTable = true;
        await LoadPageAsync(1);
    }

    /// <summary>
    /// 切换到 RVI 表。
    /// </summary>
    [RelayCommand]
    private async Task SwitchToRVI()
    {
        IsNDT = false;
        IsMainTable = true;
        await LoadPageAsync(1);
    }

    /// <summary>
    /// 切换 NDT 子表（主表或货期表）。
    /// </summary>
    /// <param name="table">表类型，"main" 为主表</param>
    [RelayCommand]
    private async Task SwitchNdtTable(string table)
    {
        IsMainTable = table == "main";
        await LoadPageAsync(1);
    }

    /// <summary>
    /// 切换 RVI 子表（Change 或 OT Code）。
    /// </summary>
    /// <param name="table">表类型，"change" 为主表</param>
    [RelayCommand]
    private async Task SwitchRviTable(string table)
    {
        IsMainTable = table == "change";
        await LoadPageAsync(1);
    }

    // —— 导入 ——

    /// <summary>
    /// 从 Excel 文件导入产品数据。
    /// </summary>
    [RelayCommand]
    private async Task Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 Excel 文件导入",
            Filter = "Excel 文件|*.xlsx;*.xls",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                IsLoading = true;
                StatusText = "导入中...";

                var count = await Task.Run(() =>
                    _importService.ImportFromExcel(dialog.FileName, GetCurrentTableName()));

                _dialog.ShowInfo($"成功导入 {count} 条产品数据", "导入成功");
                await LoadPageAsync(1);
            }
            catch (Exception ex)
            {
                _dialog.ShowError($"导入失败: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>
    /// 清除当前表的所有数据。
    /// </summary>
    [RelayCommand]
    private async Task ClearData()
    {
        if (!_dialog.ShowConfirm("确定要清除当前表的所有数据吗？此操作不可撤销。", "确认清除"))
            return;

        _productService.ClearProducts(GetCurrentTableName());
        await LoadPageAsync(1);
    }

    /// <summary>
    /// 删除指定产品。
    /// </summary>
    /// <param name="id">产品 ID</param>
    [RelayCommand]
    private async Task DeleteProduct(string id)
    {
        _productService.DeleteProduct(id, GetCurrentTableName());
        await LoadPageAsync(CurrentPage);
    }
}

/// <summary>
/// 产品数据行视图模型，表示产品表中的一行数据。
/// </summary>
public partial class ProductRowViewModel : ObservableObject
{
    /// <summary>
    /// 产品 ID。
    /// </summary>
    public string Id { get; set; } = "";
    /// <summary>
    /// 创建者。
    /// </summary>
    public string CreatedBy { get; set; } = "";
    /// <summary>
    /// 产品数据字典（列名-值）。
    /// </summary>
    public Dictionary<string, string> Data { get; set; } = new();
}
