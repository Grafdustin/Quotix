using System.IO;
using ClosedXML.Excel;
using Quotix.Common;
using Quotix.Models;

namespace Quotix.Services.Exporters;

/// <summary>
/// Excel 导出器 — 基于 quotation-template.xlsx 模板填充报价单数据
/// 对齐 WorkFast Node.js 参考实现：行插入 + 样式保留 + 公式 + 货币文本
/// </summary>
public class ExcelExporter : IExporter
{
    private static readonly string TemplatePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "quotation-template.xlsx");

    private const int DataStartRow = 17;
    private const int MaxColumn = 12;

    public string Export(Quotation quotation, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        var filename = string.IsNullOrEmpty(quotation.Filename)
            ? $"报价单_{quotation.QuoteNumber}_{DateTime.Now:yyyyMMddHHmmss}.xlsx"
            : (Path.GetExtension(quotation.Filename).Length > 0
                ? quotation.Filename
                : quotation.Filename + ".xlsx");

        var outputPath = Path.Combine(outputDir, filename);

        using var workbook = new XLWorkbook(TemplatePath);
        var ws = workbook.Worksheets.First();
        var isUsd = quotation.Currency == "USD";
        var currencyFormat = isUsd ? "$#,##0.00" : "¥#,##0.00";

        // ── 1. 公司 & 客户信息 ──
        FillHeaderInfo(ws, quotation);

        // ── 2. 产品列表 ──
        FillItems(ws, quotation, currencyFormat);

        // ── 3. 报价说明 ──
        FillNotes(ws, quotation);

        workbook.SaveAs(outputPath);
        return outputPath;
    }

    // ═══════════════════════════════════════════════
    //  表头信息
    // ═══════════════════════════════════════════════

    private static void FillHeaderInfo(IXLWorksheet ws, Quotation q)
    {
        // 公司信息
        SetCellValue(ws, "B11", q.CompanyContact);
        SetCellValue(ws, "B12", q.CompanyPhone);
        SetCellValue(ws, "B13", q.CompanyTel);
        SetCellValue(ws, "B14", q.CompanyEmail);

        // 客户信息
        SetCellValue(ws, "H11", q.CustomerName);
        SetCellValue(ws, "H12", q.CustomerContact);
        SetCellValue(ws, "H13", q.CustomerPhone);
        SetCellValue(ws, "H14", q.CustomerEmail);

        // 报价单信息
        SetCellValue(ws, "K7", q.QuoteNumber);
        SetCellValue(ws, "K9", q.QuoteDate);
    }

    // ═══════════════════════════════════════════════
    //  产品列表：插入行 + 样式保留 + 合并 + 公式
    // ═══════════════════════════════════════════════

    private static void FillItems(IXLWorksheet ws, Quotation q, string currencyFormat)
    {
        var items = q.Items;
        if (items.Count == 0) return;

        var startRow = DataStartRow;           // 17
        var isUsd = q.Currency == "USD";

        // ── 保存模板行样式（row 17） ──
        var templateRow = ws.Row(startRow);
        var templateStyles = new IXLStyle[MaxColumn + 1];
        for (int col = 1; col <= MaxColumn; col++)
            templateStyles[col] = templateRow.Cell(col).Style;

        // ── 保存原合计行样式（模板中 row 18，插入后会下移） ──
        var totalRowStyles = new IXLStyle[MaxColumn + 1];
        var originalTotalRow = ws.Row(startRow + 1);
        for (int col = 1; col <= MaxColumn; col++)
            totalRowStyles[col] = originalTotalRow.Cell(col).Style;

        // ── 根据产品数量插入行 ──
        // 产品 > 1 时在模板行下方插入，合计行自动下移
        if (items.Count > 1)
            ws.Row(startRow).InsertRowsBelow(items.Count - 1);

        // ── 填充数据行 ──
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var rowNum = startRow + i;
            var row = ws.Row(rowNum);

            // 恢复模板样式
            for (int col = 1; col <= MaxColumn; col++)
                row.Cell(col).Style = templateStyles[col];

            // 序号
            row.Cell(1).Value = i + 1;

            // 产品名称
            row.Cell(2).Value = item.ItemName;

            // 编码（C-D 合并）
            row.Cell(3).Value = item.Code ?? "";

            // 说明（E-G 合并）
            row.Cell(5).Value = item.Description ?? "";

            // 数量
            row.Cell(8).Value = item.Quantity;

            // 单价（I-J 合并），设置货币格式
            row.Cell(9).Value = item.UnitPrice;
            row.Cell(9).Style.NumberFormat.Format = currencyFormat;

            // 总价 = 数量 × 单价（K-L 合并，用公式）
            row.Cell(11).FormulaA1 = $"H{rowNum}*I{rowNum}";
            row.Cell(11).Style.NumberFormat.Format = currencyFormat;

            // 合并单元格（与模板一致）
            ws.Range(rowNum, 3, rowNum, 4).Merge();   // C-D
            ws.Range(rowNum, 5, rowNum, 7).Merge();   // E-G
            ws.Range(rowNum, 9, rowNum, 10).Merge();  // I-J
            ws.Range(rowNum, 11, rowNum, 12).Merge(); // K-L

            // 自动行高（对齐 Node.js 参考实现：中文字符宽度 + 换行 + 列宽容量估算）
            row.Height = CalculateRowHeight(item.ItemName ?? "", item.Description ?? "");
        }

        // ── 合计行 ──
        int totalRowNum = startRow + items.Count;
        var totalRow = ws.Row(totalRowNum);

        // 恢复原合计行样式
        for (int col = 1; col <= MaxColumn; col++)
            totalRow.Cell(col).Style = totalRowStyles[col];

        // J 列（= col 10）：按货币写合计文本
        totalRow.Cell(10).Value = isUsd ? "总价（美元含税）：" : "总价（人民币含税）：";

        // K-L 合并 + SUM 公式
        ws.Range(totalRowNum, 11, totalRowNum, 12).Merge();
        totalRow.Cell(11).FormulaA1 = $"SUM(K{startRow}:K{startRow + items.Count - 1})";
        totalRow.Cell(11).Style.NumberFormat.Format = currencyFormat;
    }

    // ═══════════════════════════════════════════════
    //  报价说明：标题 + 空行 + 编号列表
    // ═══════════════════════════════════════════════

    private static void FillNotes(IXLWorksheet ws, Quotation q)
    {
        int totalRowNum = DataStartRow + q.Items.Count;
        int r = totalRowNum + 2;

        // 清空模板中残留的说明文字（10 行范围）
        for (int i = r; i <= r + 10; i++)
            ws.Cell(i, 1).Value = "";

        ws.Cell(r, 1).Value = "报价说明";
        r += 2; // 跳过空行

        ws.Cell(r, 1).Value = $"1. 报价有效期：{q.Validity ?? "1个月"}";
        r += 2;

        ws.Cell(r, 1).Value = $"2. 付款方式：{q.Payment ?? "预付30%，发货前付全款"}";
        r += 2;

        ws.Cell(r, 1).Value = $"3. 交货期：{q.DeliveryTime ?? "8-12周"}";
        r += 2;

        ws.Cell(r, 1).Value = $"4. 交货方式：{q.DeliveryMethod ?? "客户项目现场，含海运、内陆运输费用及相关保险费用"}";
    }

    // ═══════════════════════════════════════════════
    //  行高计算（对齐 Node.js 参考实现）
    // ═══════════════════════════════════════════════

    private const double LineHeight = 14.0;
    private const double MinHeight = 35.5;
    private const int ItemNameLineCapacity = 13;   // B 列 ~124px，宋体 9 号约 13 个字符
    private const int DescLineCapacity = 30;        // E-G 列 ~312px，约 30 个字符

    private static double CalculateRowHeight(string itemName, string description)
    {
        int itemNameLines = CountWrappedLines(itemName, ItemNameLineCapacity);
        int descriptionLines = CountWrappedLines(description, DescLineCapacity);
        int totalLines = Math.Max(itemNameLines, descriptionLines);

        double calculatedHeight = totalLines * LineHeight;
        return Math.Max(calculatedHeight, MinHeight);
    }

    /// <summary>
    /// 计算文本在指定列宽容量下的折行数。
    /// 中文字符/中文标点算 2 个单位，其余算 1 个单位。
    /// </summary>
    private static int CountWrappedLines(string text, int lineCapacity)
    {
        if (string.IsNullOrEmpty(text))
            return 1;

        // 按换行符拆分
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        bool hasNewline = lines.Length > 1;

        int maxUnits = 0;
        foreach (var line in lines)
        {
            int units = CalculateWidthUnits(line);
            if (units > maxUnits)
                maxUnits = units;
        }

        int estimatedLines = (int)Math.Ceiling(maxUnits / (double)lineCapacity);
        return hasNewline ? lines.Length : Math.Max(estimatedLines, 1);
    }

    /// <summary>
    /// 计算文本的宽度单位：中文/中文标点 = 2，其他 = 1。
    /// </summary>
    private static int CalculateWidthUnits(string text)
    {
        int units = 0;
        foreach (char c in text)
        {
            if (IsCjkOrPunctuation(c))
                units += 2;
            else
                units += 1;
        }
        return units;
    }

    /// <summary>判断字符是否属于 CJK 字符或中文标点。</summary>
    private static bool IsCjkOrPunctuation(char c)
    {
        return c >= 0x4E00 && c <= 0x9FFF   // CJK 统一汉字
            || c >= 0x3000 && c <= 0x303F    // CJK 标点
            || c >= 0xFF00 && c <= 0xFFEF    // 全角符号
            || c >= 0x3400 && c <= 0x4DBF;   // CJK 扩展 A
    }

    // ═══════════════════════════════════════════════

    private static void SetCellValue(IXLWorksheet ws, string address, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            ws.Cell(address).Value = value;
    }
}
