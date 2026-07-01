using System.IO;
using System.Text.Json;

namespace Quotix.Services;

/// <summary>
/// 应用设置服务 — 启动时自动加载，通过属性访问，自动持久化
/// 整个程序通过 DI 注入 AppSettingsService 即可读取/修改设置
/// </summary>
public class AppSettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quotix");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private AppSettings _current;

    public AppSettingsService()
    {
        _current = LoadFromDisk();
    }

    /// <summary>当前设置（读取即最新值）</summary>
    public AppSettings Current => _current;

    /// <summary>深色模式</summary>
    public bool DarkMode
    {
        get => _current.DarkMode;
        set { _current.DarkMode = value; SaveToDisk(); }
    }

    /// <summary>导航栏折叠状态</summary>
    public bool NavigationCollapsed
    {
        get => _current.NavigationCollapsed;
        set { _current.NavigationCollapsed = value; SaveToDisk(); }
    }

    /// <summary>默认导出路径</summary>
    public string? DefaultExportPath
    {
        get => _current.DefaultExportPath;
        set { _current.DefaultExportPath = value; SaveToDisk(); }
    }

    /// <summary>获取默认导出路径（未设置时返回桌面 Quotix Exports）</summary>
    public string GetDefaultExportPath()
    {
        if (!string.IsNullOrWhiteSpace(DefaultExportPath) && Directory.Exists(DefaultExportPath))
            return DefaultExportPath;

        var defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Quotix Exports");

        if (!Directory.Exists(defaultDir))
            Directory.CreateDirectory(defaultDir);

        return defaultDir;
    }

    // ============ 内部 ============

    private static AppSettings LoadFromDisk()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new();
            }
        }
        catch { }
        return new AppSettings();
    }

    private void SaveToDisk()
    {
        try
        {
            if (!Directory.Exists(SettingsDir))
                Directory.CreateDirectory(SettingsDir);

            var json = JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }
}

public class AppSettings
{
    public bool DarkMode { get; set; }
    public bool NavigationCollapsed { get; set; }
    public string? DefaultExportPath { get; set; }
}
