using Microsoft.Data.Sqlite;

namespace Quotix.Repositories;

/// <summary>
/// Repository 基类。
/// 封装 SQL 执行、标量查询、事务管理等通用数据访问操作，消除子类重复样板代码。
/// </summary>
public abstract class BaseRepository
{
    /// <summary>数据库提供者（由子类通过构造函数注入）</summary>
    protected readonly DatabaseProvider Db;

    protected BaseRepository(DatabaseProvider db)
    {
        Db = db;
    }

    /// <summary>获取原生数据库连接（仅 Service 层复杂事务需要）</summary>
    public SqliteConnection GetConnection() => Db.GetConnection();

    // ============ 查询 ============

    /// <summary>执行查询 SQL，通过 rowMapper 映射为实体列表</summary>
    protected List<T> Query<T>(
        string sql,
        Dictionary<string, object?>? parameters,
        Func<SqliteDataReader, T> rowMapper)
    {
        var results = new List<T>();
        Db.ExecuteReader(sql, parameters, reader => results.Add(rowMapper(reader)));
        return results;
    }

    // ============ 写入 ============

    /// <summary>执行非查询 SQL（INSERT / UPDATE / DELETE）</summary>
    protected int Execute(string sql, Dictionary<string, object?>? parameters = null)
        => Db.ExecuteNonQuery(sql, parameters);

    // ============ 标量 ============

    /// <summary>执行标量查询，返回单个值</summary>
    protected T? Scalar<T>(string sql, Dictionary<string, object?>? parameters = null)
        => Db.ExecuteScalar<T>(sql, parameters);

    // ============ 事务 ============

    /// <summary>在事务中执行操作（无返回值），自动 Commit / Rollback</summary>
    protected void RunInTransaction(Action<SqliteConnection, SqliteTransaction> action)
    {
        using var conn = Db.GetConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            action(conn, tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>在事务中执行操作并返回结果，自动 Commit / Rollback</summary>
    protected T RunInTransaction<T>(Func<SqliteConnection, SqliteTransaction, T> func)
    {
        using var conn = Db.GetConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            var result = func(conn, tx);
            tx.Commit();
            return result;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
