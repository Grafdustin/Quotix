using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Quotix.Models;
using Quotix.Services;

namespace Quotix.ViewModels;

public partial class ProductDatabaseViewModel : ObservableObject
{
    private readonly ProductService _productService;
    private readonly ProductImportService _importService;
    private readonly DialogService _dialog;

    public ProductDatabaseViewModel(ProductService productService, ProductImportService importService, DialogService dialog)
    {
        _productService = productService;
        _importService = importService;
        _dialog = dialog;
    }

    private int _pendingSearchToken;

    public ObservableCollection<ProductRowViewModel> Products { get; } = new();

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isNDT = true;
    [ObservableProperty] private bool _isMainTable = true;
    [ObservableProperty] private string _currentTableLabel = "NDT - 价表";
    [ObservableProperty] private bool _isLoading;

    // —— 分页 ——
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _totalPages;
    [ObservableProperty] private int _pageSize = 50;

    private int _currentPage = 1;
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

    partial void OnTotalPagesChanged(int value)
    {
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
        NextPageCommand.NotifyCanExecuteChanged();
        PrevPageCommand.NotifyCanExecuteChanged();
        GoToFirstPageCommand.NotifyCanExecuteChanged();
        GoToLastPageCommand.NotifyCanExecuteChanged();
    }

    public bool CanGoPrev => CurrentPage > 1;
    public bool CanGoNext => CurrentPage < TotalPages;

    public ObservableCollection<string> ColumnHeaders { get; } = new();

    /// <summary>View 订阅：集合即将更新，请解绑 DataGrid</summary>
    public event Action? BeforeCollectionUpdate;
    /// <summary>View 订阅：集合更新完毕，请重新绑定 DataGrid</summary>
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

    private string GetCurrentTableName()
    {
        if (IsNDT)
            return IsMainTable ? "products_ndt" : "products_ndt_delivery";
        else
            return IsMainTable ? "products_rvi_change" : "products_rvi_ot";
    }

    public void Refresh()
    {
        _ = LoadPageAsync(1);
    }

    public async Task LoadPageAsync(int page)
    {
        IsLoading = true;
        StatusText = "加载中...";

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
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextPage()
    {
        if (CurrentPage < TotalPages)
            await LoadPageAsync(CurrentPage + 1);
    }

    [RelayCommand(CanExecute = nameof(CanGoPrev))]
    private async Task PrevPage()
    {
        if (CurrentPage > 1)
            await LoadPageAsync(CurrentPage - 1);
    }

    [RelayCommand(CanExecute = nameof(CanGoPrev))]
    private async Task GoToFirstPage() => await LoadPageAsync(1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task GoToLastPage() => await LoadPageAsync(TotalPages);

    // —— 表切换 ——
    [RelayCommand]
    private async Task SwitchToNDT()
    {
        IsNDT = true;
        IsMainTable = true;
        await LoadPageAsync(1);
    }

    [RelayCommand]
    private async Task SwitchToRVI()
    {
        IsNDT = false;
        IsMainTable = true;
        await LoadPageAsync(1);
    }

    [RelayCommand]
    private async Task SwitchNdtTable(string table)
    {
        IsMainTable = table == "main";
        await LoadPageAsync(1);
    }

    [RelayCommand]
    private async Task SwitchRviTable(string table)
    {
        IsMainTable = table == "change";
        await LoadPageAsync(1);
    }

    // —— 导入 ——
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

    [RelayCommand]
    private async Task ClearData()
    {
        if (!_dialog.ShowConfirm("确定要清除当前表的所有数据吗？此操作不可撤销。", "确认清除"))
            return;

        _productService.ClearProducts(GetCurrentTableName());
        await LoadPageAsync(1);
    }

    [RelayCommand]
    private async Task DeleteProduct(string id)
    {
        _productService.DeleteProduct(id, GetCurrentTableName());
        await LoadPageAsync(CurrentPage);
    }
}

public partial class ProductRowViewModel : ObservableObject
{
    public string Id { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public Dictionary<string, string> Data { get; set; } = new();
}
