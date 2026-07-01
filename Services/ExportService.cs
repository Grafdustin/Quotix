using System.IO;
using Quotix.Common;
using Quotix.Models;
using Quotix.Services.Exporters;

namespace Quotix.Services;

/// <summary>
/// 导出服务 — 协调 IExporter 实现，选择导出格式
/// 仅负责路径解析 + 导出器选择，具体格式由 IExporter 实现
/// </summary>
public class ExportService
{
    private readonly AppSettingsService _settings;
    private readonly ExcelExporter _excelExporter = new();

    public ExportService(AppSettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>导出报价单为 Excel</summary>
    public string ExportToExcel(Quotation quotation)
    {
        var outputDir = ResolveOutputDir();
        return _excelExporter.Export(quotation, outputDir);
    }

    /// <summary>按扩展名选择导出器（预留 PDF / 打印）</summary>
    public string Export(Quotation quotation, string format = "xlsx")
    {
        var outputDir = ResolveOutputDir();

        IExporter exporter = format.ToLowerInvariant() switch
        {
            "xlsx" or "excel" => _excelExporter,
            // "pdf" => _pdfExporter,       // 预留
            // "print" => _printExporter,   // 预留
            _ => _excelExporter
        };

        return exporter.Export(quotation, outputDir);
    }

    private string ResolveOutputDir()
    {
        if (!string.IsNullOrWhiteSpace(_settings.DefaultExportPath)
            && Directory.Exists(_settings.DefaultExportPath))
            return _settings.DefaultExportPath;

        return _settings.GetDefaultExportPath();
    }
}
