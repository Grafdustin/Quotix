using System.Reflection;

namespace Quotix;

/// <summary>
/// 应用程序信息工具类
/// </summary>
public static class AppInfo
{
    private static readonly Assembly _assembly = Assembly.GetExecutingAssembly();

    /// <summary>
    /// 产品名称
    /// </summary>
    public static string ProductName => "Quotix";

    /// <summary>
    /// 版本号 (如 "1.0.0")
    /// </summary>
    public static string Version
    {
        get
        {
            var version = _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return version ?? "1.0.0";
        }
    }

    /// <summary>
    /// 完整版本号 (如 "1.0.0+abc123")
    /// </summary>
    public static string FullVersion
    {
        get
        {
            var version = _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return version ?? "1.0.0";
        }
    }

    /// <summary>
    /// 程序集版本 (如 "1.0.0.0")
    /// </summary>
    public static string AssemblyVersion
    {
        get
        {
            var version = _assembly.GetCustomAttribute<AssemblyVersionAttribute>()?.Version;
            return version ?? "1.0.0.0";
        }
    }

    /// <summary>
    /// 文件版本 (如 "1.0.0.0")
    /// </summary>
    public static string FileVersion
    {
        get
        {
            var version = _assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
            return version ?? "1.0.0.0";
        }
    }

    /// <summary>
    /// 公司名称
    /// </summary>
    public static string Company => _assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "";

    /// <summary>
    /// 版权信息
    /// </summary>
    public static string Copyright => _assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    /// <summary>
    /// 获取格式化的版本信息字符串
    /// </summary>
    public static string GetVersionString()
    {
        return $"{ProductName} {Version}";
    }
}
