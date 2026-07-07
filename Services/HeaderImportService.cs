using System.IO;
using ClosedXML.Excel;
using Quotix.Common;
using Quotix.Models;
using Quotix.Repositories;

namespace Quotix.Services;

/// <summary>
/// 收录信息 Excel 导入服务 — 从 XLSX 解析货主/客户并写入数据库
/// </summary>
public class HeaderImportService
{
    private readonly DatabaseProvider _db;
    private readonly HeaderRepository _repo;
    private readonly CacheService _cache;

    public HeaderImportService(DatabaseProvider db, HeaderRepository repo, CacheService cache)
    {
        _db = db;
        _repo = repo;
        _cache = cache;
    }

    /// <summary>从 Excel 导入货主或客户</summary>
    public int ImportFromExcel(string filePath, string tableName)
    {
        // 先复制到临时文件，避免原文件被 Excel 等进程锁定
        string tempPath = Path.Combine(Path.GetTempPath(), $"Quotix_Import_{Guid.NewGuid()}.xlsx");
        try
        {
            File.Copy(filePath, tempPath, overwrite: true);
        }
        catch (IOException ex)
        {
            throw new IOException($"无法访问文件 '{filePath}'，请确认文件未被其他程序打开。", ex);
        }

        try
        {
            using var workbook = new XLWorkbook(tempPath);
            var worksheet = workbook.Worksheets.First();
            var rows = worksheet.RowsUsed().ToList();
            if (rows.Count < 2) return 0;

            var headerRow = rows[0];
            var headers = new List<string>();
            foreach (var cell in headerRow.Cells())
                headers.Add(cell.GetString().Trim());

            var count = 0;

            using var conn = _db.GetConnection();
            using var tx = conn.BeginTransaction();

            try
            {
                for (int i = 1; i < rows.Count; i++)
                {
                    var row = rows[i];
                    string id = IdGenerator.New();

                    if (tableName == "owners")
                    {
                        var name = GetCellValue(row, headers, "name");
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        _repo.InsertOwnerTx(conn, tx, new Owner
                        {
                            Id = id, Name = name,
                            Phone = GetCellValue(row, headers, "phone"),
                            Tel = GetCellValue(row, headers, "tel"),
                            Email = GetCellValue(row, headers, "email")
                        });
                    }
                    else if (tableName == "customers")
                    {
                        var companyName = GetCellValue(row, headers, "company_name");
                        if (string.IsNullOrWhiteSpace(companyName)) continue;

                        _repo.InsertCustomerTx(conn, tx, new Customer
                        {
                            Id = id, CompanyName = companyName,
                            Contact = GetCellValue(row, headers, "contact"),
                            Phone = GetCellValue(row, headers, "phone"),
                            Email = GetCellValue(row, headers, "email")
                        });
                    }

                    count++;
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }

            if (count > 0) _cache.InvalidateHeaders();
            return count;
        }
        finally
        {
            // 清理临时文件
            try { File.Delete(tempPath); } catch { /* ignore */ }
        }
    }

    private static string? GetCellValue(IXLRow row, List<string> headers, string columnName)
    {
        var idx = headers.FindIndex(h =>
            h.Equals(columnName, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return null;
        var val = row.Cell(idx + 1).GetString().Trim();
        return string.IsNullOrEmpty(val) ? null : val;
    }
}
