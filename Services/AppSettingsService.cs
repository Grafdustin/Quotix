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
        var settingsExists = File.Exists(SettingsPath);
        _current = LoadFromDisk();
        // 确保默认配置（如"快捷输入默认开启"）在首次运行或旧文件缺失时落盘。
        // 若用户已显式关闭，反序列化后 _current.QuickInput.Enabled 即为 false，写入不会覆盖其选择。
        if (!settingsExists)
            SaveToDisk();
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

    /// <summary>快捷输入是否启用（读写均自动持久化）</summary>
    public bool QuickInputEnabled
    {
        get => _current.QuickInput.Enabled;
        set { _current.QuickInput.Enabled = value; SaveToDisk(); }
    }

    /// <summary>快捷输入设置（含各数据库字段映射，读写均自动持久化）</summary>
    public QuickInputSettings QuickInput => _current.QuickInput;

    /// <summary>持久化快捷输入字段映射的变更</summary>
    public void SaveQuickInputSettings() => SaveToDisk();

    /// <summary>默认负责人 ID。</summary>
    public string DefaultOwnerId
    {
        get => _current.DefaultOwnerId ?? "";
        set { _current.DefaultOwnerId = value; SaveToDisk(); }
    }

    /// <summary>报价说明默认值。</summary>
    public QuotationDescriptionDefaults QuotationDescriptionDefaults => _current.QuotationDescriptionDefaults;

    /// <summary>持久化报价说明默认值。</summary>
    public void SaveQuotationDescriptionDefaults() => SaveToDisk();

    /// <summary>
    /// 获取默认导出路径。
    /// 未设置或路径不存在时，返回桌面下的"Quotix Exports"目录。
    /// </summary>
    public string GetDefaultExportPath()    {
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

    /// <summary>快捷输入设置（启用开关 + 按 NDT/RVI 分库的字段映射）</summary>
    public QuickInputSettings QuickInput { get; set; } = new();

    /// <summary>新建报价单默认负责人 ID。</summary>
    public string? DefaultOwnerId { get; set; }

    /// <summary>新建报价单默认报价说明。</summary>
    public QuotationDescriptionDefaults QuotationDescriptionDefaults { get; set; } = new();
}

/// <summary>报价说明默认值。</summary>
public class QuotationDescriptionDefaults
{
    public string Validity { get; set; } = "1个月";
    public string Payment { get; set; } = "预付30%，发货前付全款";
    public string DeliveryTime { get; set; } = "8-12周";
    public string DeliveryMethod { get; set; } = "客户项目现场，含海运、内陆运输费用及相关保险费用";
}

/// <summary>快捷输入设置实体（序列化到 settings.json）</summary>
public class QuickInputSettings
{
    /// <summary>是否启用快捷输入。关闭后报价单编号列不再触发产品快速搜索。默认开启。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 是否启用全局模糊搜索（高级分散匹配）。
    /// 开启后所有快捷搜索框与快捷窗口使用模糊匹配算法（支持字符分散匹配，如 "1-3" 匹配 "1-2-3"）；
    /// 关闭则仅使用基础前缀 / 包含匹配。默认开启。
    /// </summary>
    public bool FuzzySearch { get; set; } = true;

    /// <summary>
    /// 字段映射。外层 key 为数据库类型（"NDT" / "RVI"）；
    /// 内层 key 为报价单输入框（"编号" / "说明" / "单价"），value 为数据表列名（空字符串表示不映射）。
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> Mappings { get; set; } = new();
}
