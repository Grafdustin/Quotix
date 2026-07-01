using System.Text.Json;
using ClosedXML.Excel;
using Quotix.Common;
using Quotix.Models;
using Quotix.Repositories;

namespace Quotix.Services;

/// <summary>
/// 产品 Excel 导入服务 — 从 XLSX 解析并写入数据库
/// </summary>
public class ProductImportService
{
    private readonly DatabaseProvider _db;
    private readonly ProductRepository _repo;
    private readonly CacheService _cache;

    public ProductImportService(DatabaseProvider db, ProductRepository repo, CacheService cache)
    {
        _db = db;
        _repo = repo;
        _cache = cache;
    }

    /// <summary>从 Excel 导入产品（事务保护）</summary>
    public int ImportFromExcel(string filePath, string tableName, IProgress<int>? progress = null)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();
        var rows = worksheet.RowsUsed().ToList();
        if (rows.Count < 2) return 0;

        var headerRow = rows[0];
        var headers = new List<string>();
        foreach (var cell in headerRow.Cells())
            headers.Add(cell.GetString().Trim());

        var now = DateTime.Now.ToString(Constants.DateTimeFormat);
        var products = new List<Product>();

        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var data = new Dictionary<string, string>();
            for (int j = 0; j < headers.Count; j++)
            {
                var val = row.Cell(j + 1).GetString().Trim();
                if (!string.IsNullOrEmpty(val))
                    data[headers[j]] = val;
            }

            products.Add(new Product
            {
                Id = IdGenerator.New(),
                TableName = tableName,
                DataJson = JsonSerializer.Serialize(data),
                CreatedBy = Constants.LocalUserId,
                CreatedAt = now,
                UpdatedAt = now
            });

            progress?.Report((i + 1) * 100 / rows.Count);
        }

        using var conn = _db.GetConnection();
        using var tx = conn.BeginTransaction();

        try
        {
            foreach (var product in products)
            {
                _repo.Insert(conn, tx, product);
                _repo.InsertFts(conn, tx, product);
            }
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        _cache.InvalidateProducts();
        return products.Count;
    }
}
