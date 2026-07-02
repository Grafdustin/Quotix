using Quotix.Models;
using Quotix.Repositories;

namespace Quotix.Services;

/// <summary>
/// 内存缓存服务。
/// 启动时将数据加载到内存，搜索/报价/导出均读取缓存，大幅提升 UI 响应速度。
/// 仅在增删改时同步更新数据库和缓存。
/// </summary>
public class CacheService
{
    private readonly ProductRepository _productRepo;
    private readonly HeaderRepository _headerRepo;

    // ============ 产品缓存 ============
    private List<Product>? _allProducts;
    private bool _productsDirty = true;

    // ============ 收录信息缓存 ============
    private List<Owner>? _owners;
    private List<Customer>? _customers;
    private bool _headersDirty = true;

    public CacheService(ProductRepository productRepo, HeaderRepository headerRepo)
    {
        _productRepo = productRepo;
        _headerRepo = headerRepo;
    }

    // ============ 产品相关 ============

    /// <summary>获取全量产品（自动加载缓存，脏标记时重新读取）</summary>
    public List<Product> GetAllProducts()
    {
        if (_productsDirty || _allProducts == null)
            RefreshProducts();
        return _allProducts!;
    }

    /// <summary>按表名筛选产品（内存分页）</summary>
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

    /// <summary>
    /// FTS 全文搜索产品。
    /// 注：FTS 仍走 SQLite（内存 LIKE 无法替代全文索引的排序和分词能力）。
    /// </summary>
    public (List<Product> Products, int Total) SearchFts(
        string tableName, string keyword, int page, int pageSize)
        => _productRepo.SearchFts(tableName, keyword, page, pageSize);

    /// <summary>标记产品缓存为脏（下次读取时重新加载）</summary>
    public void InvalidateProducts() => _productsDirty = true;

    /// <summary>强制刷新产品缓存</summary>
    public void RefreshProducts()
    {
        _allProducts = _productRepo.GetAllTables();
        _productsDirty = false;
    }

    // ============ 负责人相关 ============

    /// <summary>获取所有负责人（自动加载缓存）</summary>
    public List<Owner> GetOwners()
    {
        if (_headersDirty || _owners == null)
            RefreshHeaders();
        return _owners!;
    }

    // ============ 客户相关 ============

    /// <summary>获取所有客户（自动加载缓存）</summary>
    public List<Customer> GetCustomers()
    {
        if (_headersDirty || _customers == null)
            RefreshHeaders();
        return _customers!;
    }

    /// <summary>标记收录信息缓存为脏</summary>
    public void InvalidateHeaders() => _headersDirty = true;

    /// <summary>强制刷新收录信息缓存（负责人 + 客户）</summary>
    public void RefreshHeaders()
    {
        _owners = _headerRepo.GetOwners();
        _customers = _headerRepo.GetCustomers();
        _headersDirty = false;
    }

    /// <summary>启动时预热所有缓存</summary>
    public void WarmUp()
    {
        RefreshProducts();
        RefreshHeaders();
    }
}
