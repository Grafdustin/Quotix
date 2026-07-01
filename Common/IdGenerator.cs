namespace Quotix.Common;

/// <summary>ID 生成工具 — 统一 GUID 12 位 Hex 格式</summary>
public static class IdGenerator
{
    /// <summary>生成 12 位小写十六进制 ID</summary>
    public static string New() =>
        Convert.ToHexString(Guid.NewGuid().ToByteArray())[..12].ToLower();
}
