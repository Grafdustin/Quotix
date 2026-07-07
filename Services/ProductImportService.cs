using System.IO;
using System.IO.Compression;
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
        // 先复制到安装目录下 Data 文件夹的临时文件，避免原文件被 Excel 等进程锁定
        string dataDir = AppPaths.DataDir;
        string tempPath = Path.Combine(dataDir, $"Quotix_Import_{Guid.NewGuid()}.xlsx");

        try
        {
            try
            {
                File.Copy(filePath, tempPath, overwrite: true);
            }
            catch (IOException ex)
            {
                throw new IOException($"无法访问文件 '{filePath}'，请确认文件未被其他程序打开。", ex);
            }

            // 检测 Office 密码保护（加密）
            if (IsExcelEncrypted(tempPath))
            {
                throw new InvalidOperationException("文件被加密，请解密后导入。");
            }

            using var workbook = new XLWorkbook(tempPath);
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
        finally
        {
            // 清理临时文件（无论成功或失败都会删除）
            try { File.Delete(tempPath); } catch { /* ignore */ }
        }
    }

    /// <summary>检测 Excel 文件是否启用了 Office 密码保护（加密）。</summary>
    private static bool IsExcelEncrypted(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            foreach (var entry in zip.Entries)
            {
                // 受密码保护的 xlsx 会包含 EncryptedPackage / EncryptionInfo 部件
                if (entry.FullName.Equals("EncryptedPackage", StringComparison.OrdinalIgnoreCase)
                    || entry.FullName.Equals("EncryptionInfo", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            // 不是有效的 zip 包（如旧版 .xls），交给后续流程处理
            return false;
        }
    }
}
