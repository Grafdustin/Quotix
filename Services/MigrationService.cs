using Microsoft.Data.Sqlite;
using Quotix.Repositories;

namespace Quotix.Services;

/// <summary>
/// 数据库迁移服务 — 负责建表、补列、索引、FTS 初始化
/// </summary>
public class MigrationService
{
    private readonly DatabaseProvider _db;

    public MigrationService(DatabaseProvider db)
    {
        _db = db;
    }

    public void Run()
    {
        using var conn = _db.GetConnection();

        // 1. WAL 模式（性能优化）
        using (var walCmd = conn.CreateCommand())
        {
            walCmd.CommandText = "PRAGMA journal_mode=WAL;";
            walCmd.ExecuteNonQuery();
        }

        // 2. 建表（幂等）
        CreateTables(conn);

        // 3. 补列（幂等）
        AddMissingColumns(conn);

        // 4. 索引
        CreateIndexes(conn);

        // 5. FTS 全文索引
        CreateFts(conn);

        // 6. 首次启动重建 FTS 索引
        RebuildFtsIfNeeded(conn);
    }

    private static void CreateTables(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS quotations (
                id TEXT PRIMARY KEY, quote_number TEXT, company_contact TEXT,
                company_phone TEXT, company_tel TEXT, company_email TEXT,
                customer_name TEXT NOT NULL, customer_contact TEXT, customer_phone TEXT,
                customer_email TEXT, quote_date TEXT, total_amount REAL DEFAULT 0,
                calculation_info TEXT, status TEXT DEFAULT 'draft', created_by TEXT NOT NULL,
                created_at TEXT, updated_at TEXT, currency TEXT DEFAULT 'RMB',
                validity TEXT, payment TEXT, delivery_time TEXT, delivery_method TEXT, filename TEXT
            );
            CREATE TABLE IF NOT EXISTS quotation_items (
                id TEXT PRIMARY KEY, quotation_id TEXT NOT NULL, item_name TEXT NOT NULL,
                code TEXT, description TEXT, quantity INTEGER DEFAULT 1,
                unit_price REAL DEFAULT 0, total_price REAL DEFAULT 0,
                original_price TEXT, sort_order INTEGER DEFAULT 0,
                FOREIGN KEY (quotation_id) REFERENCES quotations(id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS products (
                id TEXT PRIMARY KEY, table_name TEXT NOT NULL, data_json TEXT NOT NULL DEFAULT '{}',
                created_by TEXT, created_at TEXT, updated_at TEXT
            );
            CREATE TABLE IF NOT EXISTS owners (
                id TEXT PRIMARY KEY, name TEXT NOT NULL, phone TEXT, tel TEXT, email TEXT
            );
            CREATE TABLE IF NOT EXISTS customers (
                id TEXT PRIMARY KEY, company_name TEXT NOT NULL, contact TEXT, phone TEXT, email TEXT
            );
        ";
        cmd.ExecuteNonQuery();
    }

    private static void AddMissingColumns(SqliteConnection conn)
    {
        TryExec(conn, "ALTER TABLE products ADD COLUMN table_name TEXT NOT NULL DEFAULT ''");
        TryExec(conn, "ALTER TABLE products ADD COLUMN data_json TEXT NOT NULL DEFAULT '{}'");
        TryExec(conn, "ALTER TABLE products ADD COLUMN created_by TEXT");
        TryExec(conn, "ALTER TABLE products ADD COLUMN created_at TEXT");
        TryExec(conn, "ALTER TABLE products ADD COLUMN updated_at TEXT");
    }

    private static void CreateIndexes(SqliteConnection conn)
    {
        TryExec(conn, "CREATE INDEX IF NOT EXISTS idx_quotations_created_by ON quotations(created_by)");
        TryExec(conn, "CREATE INDEX IF NOT EXISTS idx_quotation_items_quotation_id ON quotation_items(quotation_id)");
        TryExec(conn, "CREATE INDEX IF NOT EXISTS idx_products_table_name ON products(table_name)");
        TryExec(conn, "CREATE INDEX IF NOT EXISTS idx_products_created_by ON products(created_by)");
        TryExec(conn, "CREATE INDEX IF NOT EXISTS idx_products_table_data_json ON products(table_name, data_json)");
    }

    private static void CreateFts(SqliteConnection conn)
    {
        TryExec(conn, @"CREATE VIRTUAL TABLE IF NOT EXISTS products_fts USING fts5(
            table_name, data_json, tokenize='unicode61 remove_diacritics 2'
        )");
    }

    private static void RebuildFtsIfNeeded(SqliteConnection conn)
    {
        try
        {
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM products_fts";
            var ftsCount = (long)(checkCmd.ExecuteScalar() ?? 0L);

            checkCmd.CommandText = "SELECT COUNT(*) FROM products";
            var productCount = (long)(checkCmd.ExecuteScalar() ?? 0L);

            if (ftsCount == 0 && productCount > 0)
            {
                using var rebuildCmd = conn.CreateCommand();
                rebuildCmd.CommandText = @"
                    INSERT INTO products_fts(rowid, table_name, data_json)
                    SELECT rowid, table_name, data_json FROM products";
                rebuildCmd.ExecuteNonQuery();
            }
        }
        catch { }
    }

    private static void TryExec(SqliteConnection conn, string sql)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        catch { }
    }
}
