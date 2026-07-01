using System.IO;
using ClosedXML.Excel;
using Quotix.Common;
using Quotix.Models;

namespace Quotix.Services.Exporters;

/// <summary>
/// Excel 导出器 — 基于 quotation-template.xlsx 模板填充报价单数据
/// </summary>
public class ExcelExporter : IExporter
{
    private static readonly string TemplatePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "quotation-template.xlsx");

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

        // Fill company info
        SetCellValue(ws, "B11", quotation.CompanyContact);
        SetCellValue(ws, "B12", quotation.CompanyPhone);
        SetCellValue(ws, "B13", quotation.CompanyTel);
        SetCellValue(ws, "B14", quotation.CompanyEmail);

        // Fill customer info
        SetCellValue(ws, "H11", quotation.CustomerName);
        SetCellValue(ws, "H12", quotation.CustomerContact);
        SetCellValue(ws, "H13", quotation.CustomerPhone);
        SetCellValue(ws, "H14", quotation.CustomerEmail);

        // Fill quotation info
        SetCellValue(ws, "K7", quotation.QuoteNumber);
        SetCellValue(ws, "K9", quotation.QuoteDate);

        // Fill items starting from row 17
        int startRow = 17;
        var isUsd = quotation.Currency == "USD";
        var currencyFormat = isUsd ? "$#,##0.00" : "¥#,##0.00";

        for (int i = 0; i < quotation.Items.Count; i++)
        {
            var item = quotation.Items[i];
            var row = startRow + i;

            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = item.ItemName;
            ws.Cell(row, 3).Value = item.Code ?? "";
            ws.Cell(row, 5).Value = item.Description ?? "";
            ws.Cell(row, 8).Value = item.Quantity;
            ws.Cell(row, 9).Value = item.UnitPrice;
            ws.Cell(row, 9).Style.NumberFormat.Format = currencyFormat;
            ws.Cell(row, 11).Value = item.Quantity * item.UnitPrice;
            ws.Cell(row, 11).Style.NumberFormat.Format = currencyFormat;

            ws.Range(row, 3, row, 4).Merge();
            ws.Range(row, 5, row, 7).Merge();
            ws.Range(row, 9, row, 10).Merge();
            ws.Range(row, 11, row, 12).Merge();
        }

        // Total row
        int totalRow = startRow + quotation.Items.Count;
        ws.Cell(totalRow, 1).Value = "合计";
        ws.Range(totalRow, 1, totalRow, 4).Merge();
        ws.Cell(totalRow, 11).Value = quotation.Items.Sum(i => i.Quantity * i.UnitPrice);
        ws.Cell(totalRow, 11).Style.NumberFormat.Format = currencyFormat;
        ws.Range(totalRow, 11, totalRow, 12).Merge();

        // Notes
        int notesRow = totalRow + 2;
        ws.Cell(notesRow, 1).Value = $"报价有效期: {quotation.Validity ?? "1个月"}";
        notesRow++;
        ws.Cell(notesRow, 1).Value = $"付款方式: {quotation.Payment ?? "预付30%，发货前付全款"}";
        notesRow++;
        ws.Cell(notesRow, 1).Value = $"交货期: {quotation.DeliveryTime ?? "8-12周"}";
        notesRow++;
        ws.Cell(notesRow, 1).Value = $"交货方式: {quotation.DeliveryMethod ?? "客户项目现场"}";

        workbook.SaveAs(outputPath);
        return outputPath;
    }

    private static void SetCellValue(IXLWorksheet ws, string address, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            ws.Cell(address).Value = value;
    }
}
