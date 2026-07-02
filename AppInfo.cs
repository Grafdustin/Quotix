using System.Reflection;

namespace Quotix;

/// <summary>
/// 应用程序信息工具类。
/// 提供产品名称、版本号等信息的统一访问入口。
/// </summary>
public static class AppInfo
{
    /// <summary>当前执行程序集</summary>
    private static readonly Assembly _assembly = Assembly.GetExecutingAssembly();

    /// <summary>产品名称</summary>
    public static string ProductName => "Quotix";

    /// <summary>
    /// 语义化版本号（如 "1.0.0"）。
    /// 自动截断 Git 提交哈希后缀（InformationalVersion 格式为 "1.0.0+abc123"）。
    /// </summary>
    public static string Version
    {
        get
        {
            /*
            // TODO: 测试更新功能 - 临时返回旧版本号
            return "1.0.0";
            */
            
            var raw = _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(raw))
                return "1.0.0";
            var plusIndex = raw.IndexOf('+');
            return plusIndex > 0 ? raw[..plusIndex] : raw;
        }
    }

    /// <summary>完整版本号（含 Git 哈希，如 "1.0.0+abc123"）</summary>
    public static string FullVersion
        => _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";

    /// <summary>程序集版本（如 "1.0.0.0"）</summary>
    public static string AssemblyVersion
        => _assembly.GetCustomAttribute<AssemblyVersionAttribute>()?.Version ?? "1.0.0.0";

    /// <summary>文件版本（如 "1.0.0.0"）</summary>
    public static string FileVersion
        => _assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "1.0.0.0";

    /// <summary>公司名称</summary>
    public static string Company
        => _assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "";

    /// <summary>版权信息</summary>
    public static string Copyright
        => _assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    /// <summary>获取格式化的版本信息字符串（如 "Quotix 1.0.0"）</summary>
    public static string GetVersionString()
        => $"{ProductName} {Version}";
}
