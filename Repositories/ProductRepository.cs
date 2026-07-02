using Microsoft.Data.Sqlite;
using Quotix.Common;
using Quotix.Models;

namespace Quotix.Repositories;

/// <summary>
/// 产品数据访问层。
/// 负责 products 表及 FTS 全文索引的 CRUD 操作。
/// </summary>
public class ProductRepository : BaseRepository
{
    public ProductRepository(DatabaseProvider db) : base(db) { }

    // ============ 查询 ============

    /// <summary>获取指定数据表的所有产品（按更新时间倒序）</summary>
    public List<Product> GetAll(string tableName) =>
        Query("SELECT * FROM products WHERE table_name = @table_name ORDER BY updated_at DESC",
            new() { ["@table_name"] = tableName }, ReadProduct);

    /// <summary>获取所有非空的表产品（按表名、更新时间排序）</summary>
    public List<Product> GetAllTables() =>
        Query("SELECT * FROM products WHERE table_name != '' ORDER BY table_name, updated_at DESC",
            new(), ReadProduct);

    /// <summary>分页查询指定数据表的产品</summary>
    public (List<Product> Products, int TotalCount) GetPaged(
        string tableName, string? keyword, int page, int pageSize)
    {
        var where = "WHERE table_name = @table_name";
        var p = new Dictionary<string, object?> { ["@table_name"] = tableName };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            where += " AND data_json LIKE '%' || @keyword || '%'";
            p["@keyword"] = keyword.Trim();
        }

        var total = (int)Scalar<long>($"SELECT COUNT(*) FROM products {where}", p)!;
        p["@limit"] = pageSize;
        p["@offset"] = (page - 1) * pageSize;

        var products = Query($"SELECT * FROM products {where} ORDER BY updated_at DESC LIMIT @limit OFFSET @offset",
            p, ReadProduct);

        return (products, total);
    }

    /// <summary>使用 FTS5 全文索引搜索产品</summary>
    public (List<Product> Products, int TotalCount) SearchFts(
        string tableName, string keyword, int page, int pageSize)
    {
        var p = new Dictionary<string, object?>
        {
            ["@table_name"] = tableName, ["@keyword"] = keyword.Trim(),
            ["@limit"] = pageSize, ["@offset"] = (page - 1) * pageSize
        };

        var total = (int)Scalar<long>(
            "SELECT COUNT(*) FROM products_fts WHERE table_name = @table_name AND products_fts MATCH @keyword", p)!;

        var products = Query(@"
            SELECT p.* FROM products p
            INNER JOIN products_fts fts ON p.rowid = fts.rowid
            WHERE fts.table_name = @table_name AND products_fts MATCH @keyword
            ORDER BY rank LIMIT @limit OFFSET @offset", p, ReadProduct);

        return (products, total);
    }

    // ============ 写入（事务内）============

    /// <summary>插入新产品记录（事务内调用）</summary>
    public void Insert(SqliteConnection conn, SqliteTransaction tx, Product product)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO products (id, table_name, data_json, created_by, created_at, updated_at)
            VALUES (@id, @table_name, @data_json, @created_by, @created_at, @updated_at)";
        cmd.AddParam("@id", product.Id);
        cmd.AddParam("@table_name", product.TableName);
        cmd.AddParam("@data_json", product.DataJson);
        cmd.AddParam("@created_by", product.CreatedBy);
        cmd.AddParam("@created_at", product.CreatedAt);
        cmd.AddParam("@updated_at", product.UpdatedAt);
        cmd.ExecuteNonQuery();
    }

    /// <summary>向 FTS 索引插入对应记录（事务内调用）</summary>
    public void InsertFts(SqliteConnection conn, SqliteTransaction tx, Product product)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO products_fts(rowid, table_name, data_json)
            SELECT rowid, @table_name, @data_json FROM products WHERE id = @id";
        cmd.AddParam("@id", product.Id);
        cmd.AddParam("@table_name", product.TableName);
        cmd.AddParam("@data_json", product.DataJson);
        cmd.ExecuteNonQuery();
    }

    /// <summary>检查指定产品是否存在（事务内调用）</summary>
    public bool Exists(SqliteConnection conn, SqliteTransaction tx, string id, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM products WHERE id = @id AND table_name = @table_name";
        cmd.AddParam("@id", id);
        cmd.AddParam("@table_name", tableName);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    /// <summary>更新产品数据 JSON 及更新时间</summary>
    public void Update(Product product)
    {
        product.UpdatedAt = DateTime.Now.ToString(Constants.DateTimeFormat);
        Execute("UPDATE products SET data_json = @data_json, updated_at = @updated_at WHERE id = @id AND table_name = @table_name",
            new() { ["@data_json"] = product.DataJson, ["@updated_at"] = product.UpdatedAt, ["@id"] = product.Id, ["@table_name"] = product.TableName });
    }

    /// <summary>删除指定产品（同时清理 FTS 索引）</summary>
    public void Delete(string id, string tableName) => RunInTransaction((conn, tx) =>
    {
        DeleteFts(conn, tx, id);
        DeleteRow(conn, tx, id, tableName);
    });

    /// <summary>清空指定数据表的所有产品</summary>
    public void Clear(string tableName)
    {
        RunInTransaction((conn, tx) =>
        {
            // 1. 清理 FTS 索引
            using var ftsCmd = conn.CreateCommand();
            ftsCmd.Transaction = tx;
            ftsCmd.CommandText = "DELETE FROM products_fts WHERE rowid IN (SELECT rowid FROM products WHERE table_name = @table_name)";
            ftsCmd.AddParam("@table_name", tableName);
            ftsCmd.ExecuteNonQuery();

            // 2. 清理 products 主表
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM products WHERE table_name = @table_name";
            cmd.AddParam("@table_name", tableName);
            cmd.ExecuteNonQuery();

            // 3. 删除旧的独立分表（遗留兼容）
            DropLegacyTableIfExists(conn, tx, tableName);
        });

        // 4. 重建 FTS 索引，清理内部表碎片
        RebuildFts();

        // 批量删除后回收磁盘空间
        Db.Vacuum();
    }

    /// <summary>清理孤立/空数据产品记录</summary>
    public int CleanOrphans()
    {
        var deleted = RunInTransaction((conn, tx) =>
        {
            using var ftsCmd = conn.CreateCommand();
            ftsCmd.Transaction = tx;
            ftsCmd.CommandText = "DELETE FROM products_fts WHERE rowid IN (SELECT rowid FROM products WHERE table_name = '' OR data_json = '' OR data_json = '{}')";
            ftsCmd.ExecuteNonQuery();

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM products WHERE table_name = '' OR data_json = '' OR data_json = '{}'";
            return cmd.ExecuteNonQuery();
        });

        // 批量删除后回收磁盘空间
        Db.Vacuum();
        return deleted;
    }

    // ============ 私有辅助 ============

    /// <summary>从 FTS 索引中删除指定产品</summary>
    private static void DeleteFts(SqliteConnection conn, SqliteTransaction tx, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM products_fts WHERE rowid = (SELECT rowid FROM products WHERE id = @id)";
        cmd.AddParam("@id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>从主表中删除指定产品</summary>
    private static void DeleteRow(SqliteConnection conn, SqliteTransaction tx, string id, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM products WHERE id = @id AND table_name = @table_name";
        cmd.AddParam("@id", id);
        cmd.AddParam("@table_name", tableName);
        cmd.ExecuteNonQuery();
    }

    /// <summary>删除旧的独立分表（兼容遗留数据结构）</summary>
    private static void DropLegacyTableIfExists(SqliteConnection conn, SqliteTransaction tx, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"DROP TABLE IF EXISTS [{tableName}]";
        cmd.ExecuteNonQuery();
    }

    /// <summary>重建 FTS 索引，清理内部碎片数据</summary>
    private void RebuildFts()
        => Execute("INSERT INTO products_fts(products_fts) VALUES('rebuild')");

    /// <summary>从 SqliteDataReader 读取产品实体</summary>
    public static Product ReadProduct(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        TableName = r.GetString(r.GetOrdinal("table_name")),
        DataJson = r.GetString(r.GetOrdinal("data_json")),
        CreatedBy = r.GetSafeStringOrEmpty("created_by"),
        CreatedAt = r.GetSafeDateTime("created_at"),
        UpdatedAt = r.GetSafeDateTime("updated_at")
    };
}
