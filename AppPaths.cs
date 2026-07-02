using System.IO;

namespace Quotix;

/// <summary>
/// 统一的应用路径管理 — 所有数据文件存放在安装根目录的 data\ 文件夹
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// 安装根目录（Quotix\），非 Launcher\
    /// BaseDirectory = Quotix\Launcher\ → 取 parent 得到 Quotix\
    /// </summary>
    public static string InstallationRoot
        => Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)!.FullName;

    /// <summary>
    /// Data 目录（InstallationRoot\data\），自动创建
    /// </summary>
    public static string DataDir
    {
        get
        {
            var dir = Path.Combine(InstallationRoot, "data");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>数据库文件路径（DataDir\quotation.db）</summary>
    public static string DatabasePath => Path.Combine(DataDir, "quotation.db");

    /// <summary>设置文件路径（DataDir\settings.json）</summary>
    public static string SettingsPath => Path.Combine(DataDir, "settings.json");

    /// <summary>错误日志路径（DataDir\error.log）</summary>
    public static string ErrorLogPath => Path.Combine(DataDir, "error.log");

    /// <summary>
    /// 模板数据库路径（InstallationRoot\Launcher\Resources\quotation.db）
    /// 首次运行时从此处复制到 DataDir
    /// </summary>
    public static string TemplateDatabasePath
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "quotation.db");
}
