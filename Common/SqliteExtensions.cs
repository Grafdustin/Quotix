using Microsoft.Data.Sqlite;

namespace Quotix.Common;

/// <summary>SqliteDataReader 扩展方法集，提供安全读取数据库字段的辅助方法。</summary>
public static class SqliteExtensions
{
    /// <summary>安全读取可空字符串列（null 时返回 null）</summary>
    public static string? GetSafeString(this SqliteDataReader r, string column)
    {
        var ord = r.GetOrdinal(column);
        return r.IsDBNull(ord) ? null : r.GetString(ord);
    }

    /// <summary>安全读取字符串列（null 时返回空字符串）</summary>
    public static string GetSafeStringOrEmpty(this SqliteDataReader r, string column)
    {
        var ord = r.GetOrdinal(column);
        return r.IsDBNull(ord) ? string.Empty : r.GetString(ord);
    }

    /// <summary>安全读取日期时间字符串列（空值返回 null）</summary>
    public static string? GetSafeDateTime(this SqliteDataReader r, string column)
    {
        var ord = r.GetOrdinal(column);
        if (r.IsDBNull(ord)) return null;
        var val = r.GetString(ord);
        return string.IsNullOrWhiteSpace(val) ? null : val;
    }

    /// <summary>安全读取 decimal 值（null 时返回 0）</summary>
    public static decimal GetSafeDecimal(this SqliteDataReader r, string column)
    {
        var ord = r.GetOrdinal(column);
        return r.IsDBNull(ord) ? 0m : r.GetDecimal(ord);
    }

    /// <summary>安全读取 int32 值（null 时返回 0）</summary>
    public static int GetSafeInt32(this SqliteDataReader r, string column)
    {
        var ord = r.GetOrdinal(column);
        return r.IsDBNull(ord) ? 0 : r.GetInt32(ord);
    }

    /// <summary>为 SqliteCommand 添加单个参数，自动将 null 转为 DBNull</summary>
    public static void AddParam(this SqliteCommand cmd, string name, object? value)
        => cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

    /// <summary>为 SqliteCommand 批量添加参数，自动将 null 转为 DBNull</summary>
    public static void AddParams(this SqliteCommand cmd, params (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
