using System.IO;
using Quotix.Common;
using Quotix.Models;
using Quotix.Repositories;
using Quotix.Services.Exporters;

namespace Quotix.Services;

/// <summary>
/// 导出服务。
/// 协调 IExporter 实现，负责导出格式选择和输出路径解析。
/// </summary>
public class ExportService
{
    private readonly AppSettingsService _settings;
    private readonly ExcelExporter _excelExporter = new();

    public ExportService(AppSettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>导出报价单为 Excel 文件</summary>
    public string ExportToExcel(Quotation quotation)
    {
        var outputDir = ResolveOutputDir();
        return _excelExporter.Export(quotation, outputDir);
    }

    /// <summary>按格式导出报价单（默认 Excel）</summary>
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

    /// <summary>解析输出目录（优先使用用户设置，未设置则使用桌面默认路径）</summary>
    private string ResolveOutputDir()
    {
        if (!string.IsNullOrWhiteSpace(_settings.DefaultExportPath)
            && Directory.Exists(_settings.DefaultExportPath))
            return _settings.DefaultExportPath;

        return _settings.GetDefaultExportPath();
    }
}
