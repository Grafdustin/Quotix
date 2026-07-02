using System.IO;

namespace Quotix;

/// <summary>
/// 统一的应用路径管理。
/// 所有数据文件存放在安装根目录的 Data\ 文件夹下。
/// </summary>
/// <remarks>
/// 目录结构：
/// Quotix\
/// ├── Launcher\     ← 主程序（Quotix.exe + DLL + 静态资源）
/// └── Data\         ← 所有数据文件（数据库、设置、日志）
/// </remarks>
public static class AppPaths
{
    /// <summary>安装根目录（Quotix\）。</summary>
    /// <remarks>
    /// BaseDirectory 通常为 Quotix\Launcher\，取上级目录得到安装根目录。
    /// </remarks>
    public static string InstallationRoot
    {
        get
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            return Directory.GetParent(baseDir)?.FullName ?? baseDir;
        }
    }

    /// <summary>Data 目录（InstallationRoot\Data\），不存在时自动创建。</summary>
    public static string DataDir
    {
        get
        {
            var dir = Path.Combine(InstallationRoot, "Data");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>数据库文件路径（DataDir\data.db）</summary>
    public static string DatabasePath => Path.Combine(DataDir, "data.db");

    /// <summary>设置文件路径（DataDir\settings.json）</summary>
    public static string SettingsPath => Path.Combine(DataDir, "settings.json");

    /// <summary>错误日志路径（DataDir\error.log）</summary>
    public static string ErrorLogPath => Path.Combine(DataDir, "error.log");
}
