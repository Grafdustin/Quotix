using Microsoft.Data.Sqlite;
using System.IO;

namespace Quotix.Repositories;

/// <summary>
/// SQLite 数据库连接提供者。
/// 负责连接创建、基础 SQL 执行，以及释放时 WAL 检查点刷新。
/// Schema 迁移 → <see cref="MigrationService"/>，备份恢复 → <see cref="ExportService"/>。
/// </summary>
public class DatabaseProvider : IDisposable
{
    /// <summary>是否已释放</summary>
    private bool _disposed;

    /// <summary>数据库文件路径（来自 AppPaths.DatabasePath）</summary>
    public static string DbPath => AppPaths.DatabasePath;

    /// <summary>初始化数据库目录（不存在时自动创建）</summary>
    public DatabaseProvider()
    {
        var dir = Path.GetDirectoryName(DbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>创建并返回新的 SqliteConnection（调用者负责释放）</summary>
    public SqliteConnection GetConnection()
    {
        var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        return conn;
    }

    /// <summary>执行非查询 SQL（INSERT / UPDATE / DELETE）</summary>
    public int ExecuteNonQuery(string sql, Dictionary<string, object?>? parameters = null)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        AddParameters(cmd, parameters);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>执行查询 SQL，对每一行调用 rowAction 回调</summary>
    public void ExecuteReader(
        string sql,
        Dictionary<string, object?>? parameters,
        Action<SqliteDataReader> rowAction)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        AddParameters(cmd, parameters);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            rowAction(reader);
    }

    /// <summary>执行标量查询，返回单个值（null 时返回 default）</summary>
    public T? ExecuteScalar<T>(string sql, Dictionary<string, object?>? parameters = null)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        AddParameters(cmd, parameters);
        var result = cmd.ExecuteScalar();
        if (result == null || result == DBNull.Value)
            return default;
        return (T)Convert.ChangeType(result, typeof(T));
    }

    /// <summary>为 SqliteCommand 批量添加参数，null 自动转为 DBNull</summary>
    private static void AddParameters(SqliteCommand cmd, Dictionary<string, object?>? parameters)
    {
        if (parameters == null) return;
        foreach (var kvp in parameters)
            cmd.Parameters.AddWithValue(kvp.Key, kvp.Value ?? DBNull.Value);
    }

    /// <summary>释放资源时刷新 WAL 日志，避免空闲空间浪费</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    /// <summary>
    /// 回收 SQLite 空闲磁盘空间。
    /// 重建数据库文件，将已删除数据占用的空间归还文件系统。
    /// 仅应在批量删除（Clear/CleanOrphans）后调用。
    /// </summary>
    public void Vacuum()
    {
        // 1. 先将 WAL 中待写内容刷入主数据库，避免 VACUUM 产生巨量日志
        ExecuteNonQuery("PRAGMA wal_checkpoint(TRUNCATE);");
        // 2. 重建数据库文件，回收碎片空间
        ExecuteNonQuery("VACUUM");
        // 3. VACUUM 本身是重写操作，再次 checkpoint 清理它产生的 WAL
        ExecuteNonQuery("PRAGMA wal_checkpoint(TRUNCATE);");
    }
}
