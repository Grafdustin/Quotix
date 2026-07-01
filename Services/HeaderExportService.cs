using System.Text.Json;
using Quotix.Models;
using Quotix.Repositories;

namespace Quotix.Services;

/// <summary>
/// 收录信息加密备份服务 — AES-256 加密导出/导入 JSON 备份
/// </summary>
public class HeaderExportService
{
    private readonly DatabaseProvider _db;
    private readonly HeaderRepository _repo;
    private readonly HeaderService _headerService;
    private readonly CacheService _cache;

    public HeaderExportService(DatabaseProvider db, HeaderRepository repo, HeaderService headerService, CacheService cache)
    {
        _db = db;
        _repo = repo;
        _headerService = headerService;
        _cache = cache;
    }

    /// <summary>导出货主+客户到加密文件</summary>
    public void ExportToJson(string filePath, string password)
    {
        var data = new HeaderExportData(_headerService.GetOwners(), _headerService.GetCustomers());
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        CryptoService.EncryptToFile(filePath, json, password);
    }

    /// <summary>从加密文件导入货主+客户</summary>
    public (int owners, int customers) ImportFromJson(string filePath, string password)
    {
        var json = CryptoService.DecryptFromFile(filePath, password);
        var data = JsonSerializer.Deserialize<HeaderExportData>(json)
                   ?? throw new InvalidOperationException("备份文件数据为空或格式无效");

        int ownerCount = 0, customerCount = 0;

        using var conn = _db.GetConnection();
        using var tx = conn.BeginTransaction();

        try
        {
            foreach (var owner in data.Owners)
            {
                if (string.IsNullOrWhiteSpace(owner.Name)) continue;
                if (_repo.OwnerExists(conn, tx, owner.Id)) continue;
                _repo.InsertOwnerTx(conn, tx, owner);
                ownerCount++;
            }

            foreach (var customer in data.Customers)
            {
                if (string.IsNullOrWhiteSpace(customer.CompanyName)) continue;
                if (_repo.CustomerExists(conn, tx, customer.Id)) continue;
                _repo.InsertCustomerTx(conn, tx, customer);
                customerCount++;
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        if (ownerCount > 0 || customerCount > 0) _cache.InvalidateHeaders();
        return (ownerCount, customerCount);
    }
}
