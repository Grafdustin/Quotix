using Microsoft.Data.Sqlite;

namespace Quotix.Common;

/// <summary>SqliteDataReader 扩展方法</summary>
public static class SqliteExtensions
{
    /// <summary>安全读取可空字符串列</summary>
    public static string? GetSafeString(this SqliteDataReader r, string column)
    {
        var ord = r.GetOrdinal(column);
        return r.IsDBNull(ord) ? null : r.GetString(ord);
    }

    /// <summary>安全读取字符串列（不可空）</summary>
    public static string GetSafeStringOrEmpty(this SqliteDataReader r, string column)
    {
        var ord = r.GetOrdinal(column);
        return r.IsDBNull(ord) ? string.Empty : r.GetString(ord);
    }

    /// <summary>安全读取 DateTime 字符串列</summary>
    public static string? GetSafeDateTime(this SqliteDataReader r, string column)
    {
        var ord = r.GetOrdinal(column);
        if (r.IsDBNull(ord)) return null;
        var val = r.GetString(ord);
        return string.IsNullOrWhiteSpace(val) ? null : val;
    }

    /// <summary>安全读取 decimal</summary>
    public static decimal GetSafeDecimal(this SqliteDataReader r, string column)
    {
        var ord = r.GetOrdinal(column);
        return r.IsDBNull(ord) ? 0m : r.GetDecimal(ord);
    }

    /// <summary>安全读取 int</summary>
    public static int GetSafeInt32(this SqliteDataReader r, string column)
    {
        var ord = r.GetOrdinal(column);
        return r.IsDBNull(ord) ? 0 : r.GetInt32(ord);
    }

    /// <summary>添加 SQL 参数，自动处理 DBNull</summary>
    public static void AddParam(this SqliteCommand cmd, string name, object? value)
    {
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    /// <summary>批量添加参数</summary>
    public static void AddParams(this SqliteCommand cmd, params (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
