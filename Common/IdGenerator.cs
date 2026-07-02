namespace Quotix.Common;

/// <summary>
/// ID 生成工具。
/// 统一使用 GUID 转 12 位小写十六进制字符串作为全局唯一标识。
/// </summary>
public static class IdGenerator
{
    /// <summary>生成 12 位小写十六进制 ID（基于 GUID）</summary>
    public static string New()
        => Convert.ToHexString(Guid.NewGuid().ToByteArray())[..12].ToLower();
}
