using Quotix.Models;
using Quotix.Repositories;

namespace Quotix.Services;

/// <summary>
/// 内存缓存层 — 启动时加载数据到内存，搜索/报价/导出全部读缓存
/// 仅在增删改时同步更新数据库+缓存，大幅提升 UI 响应速度
/// </summary>
public class CacheService
{
    private readonly ProductRepository _productRepo;
    private readonly HeaderRepository _headerRepo;

    // ============ Products ============
    private List<Product>? _allProducts;
    private bool _productsDirty = true;

    // ============ Owners / Customers ============
    private List<Owner>? _owners;
    private List<Customer>? _customers;
    private bool _headersDirty = true;

    public CacheService(ProductRepository productRepo, HeaderRepository headerRepo)
    {
        _productRepo = productRepo;
        _headerRepo = headerRepo;
    }

    // ============ Products ============

    /// <summary>获取全量产品（自动加载缓存）</summary>
    public List<Product> GetAllProducts()
    {
        if (_productsDirty || _allProducts == null)
            RefreshProducts();
        return _allProducts!;
    }

    /// <summary>按表名筛选（分页）</summary>
    public (List<Product> Products, int Total) GetByTable(
        string tableName, string? keyword, int page, int pageSize)
    {
        var all = GetAllProducts();
        var filtered = all.Where(p => p.TableName == tableName).ToList();

        if (!string.IsNullOrWhiteSpace(keyword))
            filtered = filtered.Where(p =>
                p.DataJson.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var total = filtered.Count;
        var paged = filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (paged, total);
    }

    /// <summary>FTS 搜索</summary>
    public (List<Product> Products, int Total) SearchFts(
        string tableName, string keyword, int page, int pageSize)
    {
        // FTS 仍走 SQLite（内存 LIKE 无法替代全文索引的排序和分词）
        return _productRepo.SearchFts(tableName, keyword, page, pageSize);
    }

    /// <summary>标记产品缓存过期</summary>
    public void InvalidateProducts() => _productsDirty = true;

    /// <summary>强制刷新产品缓存</summary>
    public void RefreshProducts()
    {
        _allProducts = _productRepo.GetAllTables();
        _productsDirty = false;
    }

    // ============ Owners ============

    public List<Owner> GetOwners()
    {
        if (_headersDirty || _owners == null)
            RefreshHeaders();
        return _owners!;
    }

    // ============ Customers ============

    public List<Customer> GetCustomers()
    {
        if (_headersDirty || _customers == null)
            RefreshHeaders();
        return _customers!;
    }

    /// <summary>标记收录信息缓存过期</summary>
    public void InvalidateHeaders() => _headersDirty = true;

    /// <summary>强制刷新收录信息缓存</summary>
    public void RefreshHeaders()
    {
        _owners = _headerRepo.GetOwners();
        _customers = _headerRepo.GetCustomers();
        _headersDirty = false;
    }

    /// <summary>全部预热（启动时调用）</summary>
    public void WarmUp()
    {
        RefreshProducts();
        RefreshHeaders();
    }
}
