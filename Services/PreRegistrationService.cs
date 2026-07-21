using System.IO;
using ClosedXML.Excel;
using Quotix.Common;
using Quotix.Models;
using Quotix.Repositories;

namespace Quotix.Services;

/// <summary>
/// NDT 预报备单保存和导出服务。
/// </summary>
public class PreRegistrationService
{
    private static readonly string TemplatePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "ndt-pre-registration-template.xlsx");

    private readonly PreRegistrationRepository _repo;
    private readonly AppSettingsService _settings;

    public PreRegistrationService(PreRegistrationRepository repo, AppSettingsService settings)
    {
        _repo = repo;
        _settings = settings;
    }

    public List<PreRegistration> GetAll() => _repo.GetAll();

    public PreRegistration? GetById(string id) => _repo.GetById(id);

    public PreRegistration Create(PreRegistration item)
    {
        var now = DateTime.Now.ToString(Constants.DateTimeFormat);
        item.Id = IdGenerator.New();
        item.CreatedAt = now;
        item.UpdatedAt = now;
        item.RegistrationDate = string.IsNullOrWhiteSpace(item.RegistrationDate)
            ? DateTime.Now.ToString("yyyy-MM-dd")
            : item.RegistrationDate;
        item.Title = BuildTitle(item);
        _repo.Insert(item);
        return item;
    }

    public void Delete(string id) => _repo.Delete(id);

    public string Export(string id)
    {
        var item = GetById(id) ?? throw new InvalidOperationException("未找到报备记录。");
        return Export(item);
    }

    public string Export(PreRegistration item)
    {
        if (!File.Exists(TemplatePath))
            throw new FileNotFoundException("未找到 NDT 预报备模板。", TemplatePath);

        var outputDir = ResolveOutputDir();
        Directory.CreateDirectory(outputDir);
        var filename = MakeSafeFileName($"{BuildTitle(item)}.xlsx");
        var outputPath = Path.Combine(outputDir, filename);

        using var workbook = new XLWorkbook(TemplatePath);
        var ws = workbook.Worksheets.First();

        Set(ws, "C23", item.AgentName);
        Set(ws, "F23", item.RegistrationDate);
        Set(ws, "B24", item.AgentSales);
        Set(ws, "D24", item.AgentPhone);
        Set(ws, "G24", item.AgentEmail);

        Set(ws, "B25", item.MiddlemanName);
        Set(ws, "F25", item.MiddlemanAddress);
        Set(ws, "B26", item.MiddlemanSales);
        Set(ws, "D26", item.MiddlemanPhone);
        Set(ws, "G26", item.MiddlemanEmail);

        Set(ws, "B28", item.CustomerName);
        Set(ws, "F28", item.CustomerTel);
        Set(ws, "B29", item.CustomerAddress);
        Set(ws, "F29", item.CustomerFax);
        Set(ws, "B30", item.CustomerDepartment);
        Set(ws, "F30", item.CustomerEmail);
        Set(ws, "B31", item.CustomerContact);
        Set(ws, "F31", item.CustomerMobile);
        Set(ws, "B32", item.IndustryMarket);
        Set(ws, "F32", item.InformationSource);

        Set(ws, "A34", item.ApplicationPurpose);
        Set(ws, "A38", item.RecommendedProducts);
        Set(ws, "A41", item.CompetitorInfo);

        Set(ws, "B46", item.ActivityDate1);
        Set(ws, "D46", item.ActivityContent1);
        Set(ws, "B47", item.ActivityDate2);
        Set(ws, "D47", item.ActivityContent2);
        Set(ws, "B48", item.ActivityDate3);
        Set(ws, "D48", item.ActivityContent3);
        Set(ws, "B49", item.ActivityDate4);
        Set(ws, "D49", item.ActivityContent4);
        Set(ws, "B50", item.ActivityDate5);
        Set(ws, "D50", item.ActivityContent5);
        Set(ws, "B51", item.ActivityDate6);
        Set(ws, "D51", item.ActivityContent6);
        Set(ws, "B52", item.CaseResult);

        workbook.SaveAs(outputPath);
        return outputPath;
    }

    private string ResolveOutputDir()
    {
        if (!string.IsNullOrWhiteSpace(_settings.DefaultExportPath) && Directory.Exists(_settings.DefaultExportPath))
            return _settings.DefaultExportPath;
        return _settings.GetDefaultExportPath();
    }

    private static void Set(IXLWorksheet ws, string address, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        ws.Cell(address).Value = value;
    }

    private static string BuildTitle(PreRegistration item)
    {
        var agent = string.IsNullOrWhiteSpace(item.AgentName) ? "代理商" : item.AgentName.Trim();
        var customer = string.IsNullOrWhiteSpace(item.CustomerName) ? "客户" : item.CustomerName.Trim();
        return $"{agent}---{customer}";
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? ' ' : ch).ToArray());
        return string.Join(" ", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
