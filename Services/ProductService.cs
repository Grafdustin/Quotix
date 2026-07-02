using Quotix.Models;
using Quotix.Repositories;

namespace Quotix.Services;

/// <summary>
/// 产品业务逻辑层。
/// 负责产品 CRUD 及搜索，导入/导出已拆分到独立 Service。
/// </summary>
public class ProductService
{
    private readonly ProductRepository _repo;
    private readonly CacheService _cache;

    public ProductService(ProductRepository repo, CacheService cache)
    {
        _repo = repo;
        _cache = cache;
    }

    // ============ 查询（委托 Repository + Cache）============

    /// <summary>获取指定数据表的所有产品</summary>
    public List<Product> GetProducts(string tableName) => _repo.GetAll(tableName);

    /// <summary>分页查询指定数据表的产品（支持关键词过滤）</summary>
    public (List<Product> Products, int TotalCount) GetProductsPaged(
        string tableName, string? keyword, int page, int pageSize)
        => _repo.GetPaged(tableName, keyword, page, pageSize);

    /// <summary>FTS 全文搜索产品</summary>
    public (List<Product> Products, int TotalCount) SearchProductsFts(
        string tableName, string keyword, int page, int pageSize)
        => _repo.SearchFts(tableName, keyword, page, pageSize);

    /// <summary>获取所有表的产品（用于导出等场景）</summary>
    public List<Product> GetAllProductsPublic() => _repo.GetAllTables();

    // ============ 写入（同步缓存）============

    /// <summary>更新产品，同时使缓存失效</summary>
    public void UpdateProduct(Product product)
    {
        _repo.Update(product);
        _cache.InvalidateProducts();
    }

    /// <summary>删除产品，同时使缓存失效</summary>
    public void DeleteProduct(string id, string tableName)
    {
        _repo.Delete(id, tableName);
        _cache.InvalidateProducts();
    }

    /// <summary>清空指定数据表，同时使缓存失效</summary>
    public void ClearProducts(string tableName)
    {
        _repo.Clear(tableName);
        _cache.InvalidateProducts();
    }

    /// <summary>清理孤立/空数据产品，返回删除数量</summary>
    public int CleanOrphanedProducts()
    {
        var count = _repo.CleanOrphans();
        if (count > 0) _cache.InvalidateProducts();
        return count;
    }
}
