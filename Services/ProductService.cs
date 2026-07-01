using Quotix.Models;
using Quotix.Repositories;

namespace Quotix.Services;

/// <summary>
/// 产品业务逻辑层 — CRUD + 搜索（导入/导出已拆分到 ProductImportService / ProductExportService）
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

    // ============ 查询 ============

    public List<Product> GetProducts(string tableName) => _repo.GetAll(tableName);

    public (List<Product> Products, int TotalCount) GetProductsPaged(
        string tableName, string? keyword, int page, int pageSize)
        => _repo.GetPaged(tableName, keyword, page, pageSize);

    public (List<Product> Products, int TotalCount) SearchProductsFts(
        string tableName, string keyword, int page, int pageSize)
        => _repo.SearchFts(tableName, keyword, page, pageSize);

    public List<Product> GetAllProductsPublic() => _repo.GetAllTables();

    // ============ 写入（含缓存失效）============

    public void UpdateProduct(Product product)
    {
        _repo.Update(product);
        _cache.InvalidateProducts();
    }

    public void DeleteProduct(string id, string tableName)
    {
        _repo.Delete(id, tableName);
        _cache.InvalidateProducts();
    }

    public void ClearProducts(string tableName)
    {
        _repo.Clear(tableName);
        _cache.InvalidateProducts();
    }

    public int CleanOrphanedProducts()
    {
        var count = _repo.CleanOrphans();
        if (count > 0) _cache.InvalidateProducts();
        return count;
    }
}
