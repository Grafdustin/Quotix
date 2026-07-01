using System.Text.Json;
using System.Text.Json.Serialization;
using Quotix.Models;
using Quotix.Repositories;

namespace Quotix.Services;

/// <summary>
/// 产品加密备份服务 — AES-256 加密导出/导入 JSON 备份
/// </summary>
public class ProductExportService
{
    private readonly DatabaseProvider _db;
    private readonly ProductRepository _repo;
    private readonly CacheService _cache;

    public ProductExportService(DatabaseProvider db, ProductRepository repo, CacheService cache)
    {
        _db = db;
        _repo = repo;
        _cache = cache;
    }

    /// <summary>导出全量产品到加密文件</summary>
    public void ExportToJson(string filePath, string password)
    {
        var products = _repo.GetAllTables();
        var json = JsonSerializer.Serialize(products, new JsonSerializerOptions { WriteIndented = true });
        CryptoService.EncryptToFile(filePath, json, password);
    }

    /// <summary>从加密文件导入产品</summary>
    public int ImportFromJson(string filePath, string password, IProgress<int>? progress = null)
    {
        progress?.Report(10);
        var json = CryptoService.DecryptFromFile(filePath, password);
        progress?.Report(30);
        var products = JsonSerializer.Deserialize<List<Product>>(json)
                       ?? throw new InvalidOperationException("备份文件数据为空或格式无效");
        return ImportProductsFromBackup(products, progress);
    }

    /// <summary>导出全量数据（产品+报价+收录）— 全流式，无大字符串</summary>
    public void ExportAllData(string filePath, string password, FullBackupData backup)
    {
        CryptoService.EncryptToFile(filePath, backup, password);
    }

    /// <summary>导入全量备份数据</summary>
    public FullBackupData ImportAllData(string filePath, string password)
    {
        var json = CryptoService.DecryptFromFile(filePath, password);
        return JsonSerializer.Deserialize<FullBackupData>(json)
               ?? throw new InvalidOperationException("备份文件数据为空或格式无效");
    }

    /// <summary>批量导入（跳过重复和无效记录）</summary>
    public int ImportProductsFromBackup(List<Product> products, IProgress<int>? progress = null)
    {
        if (products.Count == 0) return 0;

        using var conn = _db.GetConnection();
        using var tx = conn.BeginTransaction();
        var count = 0;
        var total = products.Count;

        try
        {
            for (int i = 0; i < total; i++)
            {
                var product = products[i];
                if (string.IsNullOrWhiteSpace(product.TableName)) continue;
                if (string.IsNullOrWhiteSpace(product.DataJson) || product.DataJson == "{}") continue;
                if (_repo.Exists(conn, tx, product.Id, product.TableName)) continue;

                _repo.Insert(conn, tx, product);
                _repo.InsertFts(conn, tx, product);
                count++;

                progress?.Report(30 + (i + 1) * 70 / total);
            }
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        if (count > 0) _cache.InvalidateProducts();
        return count;
    }
}
