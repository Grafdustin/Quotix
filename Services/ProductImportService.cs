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

    /// <summary>读取 Excel 中可导入的工作表名称。</summary>
    public IReadOnlyList<string> GetWorksheetNames(string filePath)
    {
        try
        {
            using var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var ms = new MemoryStream();
            fileStream.CopyTo(ms);
            ms.Position = 0;

            using var workbook = new XLWorkbook(ms);
            return workbook.Worksheets.Select(worksheet => worksheet.Name).ToList();
        }
        catch (IOException ex)
        {
            throw new IOException($"无法访问文件 '{filePath}'，请确认文件仍然存在。", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "无法读取 Excel 工作表，请确认文件未加密且为有效的 .xlsx 格式。",
                ex);
        }
    }

    /// <summary>从 Excel 的指定工作表导入产品（事务保护）</summary>
    public int ImportFromExcel(
        string filePath,
        string tableName,
        IProgress<int>? progress = null,
        string? worksheetName = null)
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

            try
            {
                // 检测 Office 密码保护（加密）
                if (IsExcelEncrypted(tempPath))
                {
                    throw new InvalidOperationException("文件被加密，请解密后导入。");
                }

                // 读入内存流再打开：避免 XLWorkbook 持有文件句柄，导致异常时临时文件无法删除
                using var fileStream = File.OpenRead(tempPath);
                using var ms = new MemoryStream();
                fileStream.CopyTo(ms);
                ms.Position = 0;

                using var workbook = new XLWorkbook(ms);
                var worksheet = string.IsNullOrWhiteSpace(worksheetName)
                    ? workbook.Worksheets.First()
                    : workbook.Worksheets.FirstOrDefault(
                        item => string.Equals(item.Name, worksheetName, StringComparison.Ordinal))
                      ?? throw new InvalidOperationException($"工作表“{worksheetName}”不存在，请重新选择文件。");
                var rows = worksheet.RowsUsed().ToList();
                if (rows.Count < 2) return 0;

                var headerRow = FindHeaderRow(worksheet, rows);
                var firstColumn = worksheet.FirstColumnUsed()?.ColumnNumber() ?? 1;
                var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? firstColumn;
                var headers = new List<(int ColumnNumber, string Name)>();
                for (int column = firstColumn; column <= lastColumn; column++)
                {
                    var name = headerRow.Cell(column).GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                        headers.Add((column, name));
                }

                if (headers.Count == 0) return 0;

                var now = DateTime.Now.ToString(Constants.DateTimeFormat);
                var products = new List<Product>();
                var dataRows = rows
                    .Where(row => row.RowNumber() > headerRow.RowNumber())
                    .ToList();

                for (int i = 0; i < dataRows.Count; i++)
                {
                    var row = dataRows[i];
                    var data = new Dictionary<string, string>();
                    foreach (var header in headers)
                    {
                        var val = row.Cell(header.ColumnNumber).GetString().Trim();
                        if (!string.IsNullOrEmpty(val))
                            data[header.Name] = val;
                    }

                    if (data.Count == 0)
                        continue;

                    products.Add(new Product
                    {
                        Id = IdGenerator.New(),
                        TableName = tableName,
                        DataJson = JsonSerializer.Serialize(data),
                        CreatedBy = Constants.LocalUserId,
                        CreatedAt = now,
                        UpdatedAt = now
                    });

                    progress?.Report((i + 1) * 100 / dataRows.Count);
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
            catch (InvalidOperationException) { throw; }   // 加密等明确提示，原样上抛
            catch (IOException) { throw; }                 // 文件访问错误，原样上抛
            catch (Exception ex)
            {
                // 打开/解析阶段异常：多因漏检的加密文件或损坏文件，给出友好提示
                throw new InvalidOperationException("无法读取 Excel 文件，请确认文件未加密且为有效的 .xlsx 格式。", ex);
            }
        }
        finally
        {
            // 清理临时文件（无论成功或失败都会删除）
            SafeDeleteTempFile(tempPath);
        }
    }

    private static IXLRow FindHeaderRow(IXLWorksheet worksheet, IReadOnlyList<IXLRow> rows)
    {
        if (worksheet.AutoFilter.IsEnabled && worksheet.AutoFilter.Range != null)
        {
            var rowNumber = worksheet.AutoFilter.Range.RangeAddress.FirstAddress.RowNumber;
            return worksheet.Row(rowNumber);
        }

        var firstColumn = worksheet.FirstColumnUsed()?.ColumnNumber() ?? 1;
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? firstColumn;
        var candidates = rows
            .Take(25)
            .Select(row =>
            {
                var cells = row.Cells(firstColumn, lastColumn)
                    .Where(cell => !string.IsNullOrWhiteSpace(cell.GetString()))
                    .ToList();
                return new
                {
                    Row = row,
                    CellCount = cells.Count,
                    BoldCellCount = cells.Count(cell => cell.Style.Font.Bold)
                };
            })
            .Where(candidate => candidate.CellCount > 0)
            .ToList();

        if (candidates.Count == 0)
            return rows[0];

        var maximumCellCount = candidates.Max(candidate => candidate.CellCount);
        var minimumHeaderCells = Math.Max(2, (int)Math.Ceiling(maximumCellCount * 0.6));
        var likelyHeaders = candidates
            .Where(candidate => candidate.CellCount >= minimumHeaderCells)
            .ToList();

        return likelyHeaders.FirstOrDefault(candidate => candidate.BoldCellCount >= 2)?.Row
               ?? likelyHeaders.FirstOrDefault()?.Row
               ?? candidates[0].Row;
    }

    /// <summary>检测 Excel 文件是否启用了 Office 密码保护（加密）。</summary>
    /// <remarks>
    /// 覆盖两种常见加密形式：
    /// 1. OOXML 加密（.xlsx）：zip 包内含 EncryptedPackage / EncryptionInfo 部件；
    /// 2. 旧版复合文档加密（.xls）：文件头为 OLE Compound File 魔数（D0 CF 11 E0 A1 B1 1A E1）。
    /// </remarks>
    private static bool IsExcelEncrypted(string path)
    {
        // 情况 1：OOXML 加密（.xlsx）
        try
        {
            using var zip = ZipFile.OpenRead(path);
            foreach (var entry in zip.Entries)
            {
                if (entry.Name.Equals("EncryptedPackage", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals("EncryptionInfo", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch (InvalidDataException)
        {
            // 不是合法 zip：检查是否为旧版 OLE 复合文档（加密 .xls）
            try
            {
                var header = new byte[8];
                using var fs = File.OpenRead(path);
                if (fs.Read(header, 0, header.Length) == header.Length
                    && header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0x11 && header[3] == 0xE0
                    && header[4] == 0xA1 && header[5] == 0xB1 && header[6] == 0x1A && header[7] == 0xE1)
                {
                    return true;
                }
            }
            catch { /* ignore */ }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>安全删除导入临时文件：即使被占用也尽量清理，失败则忽略，避免残留。</summary>
    private static void SafeDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 极端情况下文件仍被占用，忽略以免阻塞主流程
        }
    }
}
