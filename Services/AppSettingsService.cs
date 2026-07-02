using System.IO;
using System.Text.Json;

namespace Quotix.Services;

/// <summary>
/// 应用设置服务。
/// 负责设置的加载、持久化和属性访问，通过 DI 注入全局使用。
/// </summary>
public class AppSettingsService
{
    /// <summary>设置文件目录（AppPaths.DataDir）</summary>
    private static readonly string SettingsDir = AppPaths.DataDir;

    /// <summary>设置文件路径（DataDir\settings.json）</summary>
    private static readonly string SettingsPath = AppPaths.SettingsPath;

    /// <summary>当前设置对象（内存缓存）</summary>
    private AppSettings _current;

    public AppSettingsService()
    {
        _current = LoadFromDisk();
    }

    /// <summary>当前设置（读取即最新值）</summary>
    public AppSettings Current => _current;

    /// <summary>深色模式开关（读写均自动持久化）</summary>
    public bool DarkMode
    {
        get => _current.DarkMode;
        set { _current.DarkMode = value; SaveToDisk(); }
    }

    /// <summary>导航栏折叠状态（读写均自动持久化）</summary>
    public bool NavigationCollapsed
    {
        get => _current.NavigationCollapsed;
        set { _current.NavigationCollapsed = value; SaveToDisk(); }
    }

    /// <summary>默认导出路径（读写均自动持久化）</summary>
    public string? DefaultExportPath
    {
        get => _current.DefaultExportPath;
        set { _current.DefaultExportPath = value; SaveToDisk(); }
    }

    /// <summary>是否启用自动检测更新（读写均自动持久化，默认开启）</summary>
    public bool AutoUpdateEnabled
    {
        get => _current.AutoUpdateEnabled;
        set { _current.AutoUpdateEnabled = value; SaveToDisk(); }
    }

    /// <summary>
    /// 获取默认导出路径。
    /// 未设置或路径不存在时，返回桌面下的"Quotix Exports"目录。
    /// </summary>
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

    // ============ 私有方法 ============

    /// <summary>从磁盘加载设置文件，失败返回默认设置</summary>
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

    /// <summary>将当前设置序列化并写入磁盘</summary>
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

/// <summary>应用设置实体（序列化到 settings.json）</summary>
public class AppSettings
{
    /// <summary>是否启用深色模式</summary>
    public bool DarkMode { get; set; }

    /// <summary>导航栏是否折叠</summary>
    public bool NavigationCollapsed { get; set; }

    /// <summary>用户设置的默认导出路径</summary>
    public string? DefaultExportPath { get; set; }

    /// <summary>是否启用自动检测更新（默认开启）</summary>
    public bool AutoUpdateEnabled { get; set; } = true;
}
