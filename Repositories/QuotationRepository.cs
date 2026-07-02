using Microsoft.Data.Sqlite;
using Quotix.Common;
using Quotix.Models;

namespace Quotix.Repositories;

/// <summary>
/// 报价单数据访问层。
/// 负责 quotations 及 quotation_items 表的 CRUD 操作。
/// </summary>
public class QuotationRepository : BaseRepository
{
    public QuotationRepository(DatabaseProvider db) : base(db) { }

    // ============ 查询 ============

    /// <summary>获取指定用户的所有报价单（按创建时间倒序）</summary>
    public List<Quotation> GetAll(string createdBy) =>
        Query("SELECT * FROM quotations WHERE created_by = @created_by ORDER BY created_at DESC",
            new() { ["@created_by"] = createdBy }, ReadQuotation);

    /// <summary>获取所有报价单及其明细项</summary>
    public List<Quotation> GetAllWithItems()
    {
        var quotations = Query("SELECT * FROM quotations ORDER BY created_at DESC", null, ReadQuotation);
        if (quotations.Count == 0) return quotations;

        var allItems = Query("SELECT * FROM quotation_items ORDER BY sort_order", null, ReadQuotationItem);
        var map = allItems.GroupBy(i => i.QuotationId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var q in quotations)
            q.Items = map.TryGetValue(q.Id, out var items) ? items : new();

        return quotations;
    }

    /// <summary>根据 ID 获取报价单及其所有明细项</summary>
    public Quotation? GetById(string id)
    {
        var quotations = Query("SELECT * FROM quotations WHERE id = @id",
            new() { ["@id"] = id }, ReadQuotation);
        var q = quotations.FirstOrDefault();
        if (q == null) return null;

        q.Items = Query("SELECT * FROM quotation_items WHERE quotation_id = @quotation_id ORDER BY sort_order",
            new() { ["@quotation_id"] = id }, ReadQuotationItem);
        return q;
    }

    // ============ 写入（事务内）============

    /// <summary>插入报价单主记录（事务内调用）</summary>
    public void InsertQuotation(SqliteConnection conn, SqliteTransaction tx, Quotation q)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO quotations (id, quote_number, company_contact, company_phone, company_tel, company_email,
                customer_name, customer_contact, customer_phone, customer_email, quote_date,
                total_amount, calculation_info, status, created_by, created_at, updated_at,
                currency, validity, payment, delivery_time, delivery_method, filename)
            VALUES (@id, @quote_number, @company_contact, @company_phone, @company_tel, @company_email,
                @customer_name, @customer_contact, @customer_phone, @customer_email, @quote_date,
                @total_amount, @calculation_info, @status, @created_by, @created_at, @updated_at,
                @currency, @validity, @payment, @delivery_time, @delivery_method, @filename)";
        AddQuotationParams(cmd, q);
        cmd.AddParam("@id", q.Id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>插入报价单明细项（事务内调用）</summary>
    public void InsertItem(SqliteConnection conn, SqliteTransaction tx, QuotationItem item)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO quotation_items (id, quotation_id, item_name, code, description,
                quantity, unit_price, total_price, original_price, sort_order)
            VALUES (@id, @quotation_id, @item_name, @code, @description,
                @quantity, @unit_price, @total_price, @original_price, @sort_order)";
        cmd.AddParam("@id", item.Id);
        cmd.AddParam("@quotation_id", item.QuotationId);
        cmd.AddParam("@item_name", item.ItemName);
        cmd.AddParam("@code", item.Code);
        cmd.AddParam("@description", item.Description);
        cmd.AddParam("@quantity", item.Quantity);
        cmd.AddParam("@unit_price", item.UnitPrice);
        cmd.AddParam("@total_price", item.TotalPrice);
        cmd.AddParam("@original_price", item.OriginalPrice);
        cmd.AddParam("@sort_order", item.SortOrder);
        cmd.ExecuteNonQuery();
    }

    /// <summary>更新报价单主记录（事务内调用）</summary>
    public void UpdateQuotation(SqliteConnection conn, SqliteTransaction tx, Quotation q)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"UPDATE quotations SET
            quote_number=@quote_number, company_contact=@company_contact, company_phone=@company_phone,
            company_tel=@company_tel, company_email=@company_email, customer_name=@customer_name,
            customer_contact=@customer_contact, customer_phone=@customer_phone, customer_email=@customer_email,
            quote_date=@quote_date, total_amount=@total_amount, calculation_info=@calculation_info,
            status=@status, updated_at=@updated_at, currency=@currency, validity=@validity,
            payment=@payment, delivery_time=@delivery_time, delivery_method=@delivery_method, filename=@filename
            WHERE id=@id";
        AddQuotationParams(cmd, q);
        cmd.AddParam("@id", q.Id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>删除报价单的所有明细项（事务内调用）</summary>
    public void DeleteItems(SqliteConnection conn, SqliteTransaction tx, string quotationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM quotation_items WHERE quotation_id = @quotation_id";
        cmd.AddParam("@quotation_id", quotationId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>根据 ID 删除报价单主记录</summary>
    public void Delete(string id) =>
        Execute("DELETE FROM quotations WHERE id = @id", new() { ["@id"] = id });

    // ============ 实体映射 ============

    /// <summary>从 SqliteDataReader 读取报价单实体</summary>
    public static Quotation ReadQuotation(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        QuoteNumber = r.GetSafeString("quote_number"),
        CompanyContact = r.GetSafeString("company_contact"),
        CompanyPhone = r.GetSafeString("company_phone"),
        CompanyTel = r.GetSafeString("company_tel"),
        CompanyEmail = r.GetSafeString("company_email"),
        CustomerName = r.GetString(r.GetOrdinal("customer_name")),
        CustomerContact = r.GetSafeString("customer_contact"),
        CustomerPhone = r.GetSafeString("customer_phone"),
        CustomerEmail = r.GetSafeString("customer_email"),
        QuoteDate = r.GetSafeString("quote_date"),
        TotalAmount = r.GetSafeDecimal("total_amount"),
        CalculationInfo = r.GetSafeString("calculation_info"),
        Status = r.GetString(r.GetOrdinal("status")),
        CreatedBy = r.GetString(r.GetOrdinal("created_by")),
        CreatedAt = r.GetSafeDateTime("created_at"),
        UpdatedAt = r.GetSafeDateTime("updated_at"),
        Currency = r.GetSafeString("currency"),
        Validity = r.GetSafeString("validity"),
        Payment = r.GetSafeString("payment"),
        DeliveryTime = r.GetSafeString("delivery_time"),
        DeliveryMethod = r.GetSafeString("delivery_method"),
        Filename = r.GetSafeString("filename")
    };

    /// <summary>从 SqliteDataReader 读取报价单明细项实体</summary>
    public static QuotationItem ReadQuotationItem(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        QuotationId = r.GetString(r.GetOrdinal("quotation_id")),
        ItemName = r.GetString(r.GetOrdinal("item_name")),
        Code = r.GetSafeString("code"),
        Description = r.GetSafeString("description"),
        Quantity = r.GetSafeInt32("quantity"),
        UnitPrice = r.GetSafeDecimal("unit_price"),
        TotalPrice = r.GetSafeDecimal("total_price"),
        OriginalPrice = r.GetSafeString("original_price"),
        SortOrder = r.GetSafeInt32("sort_order")
    };

    // ============ SQL 参数绑定 ============

    /// <summary>为 SqliteCommand 绑定报价单实体的所有参数</summary>
    private static void AddQuotationParams(SqliteCommand cmd, Quotation q)
    {
        cmd.AddParam("@quote_number", q.QuoteNumber);
        cmd.AddParam("@company_contact", q.CompanyContact);
        cmd.AddParam("@company_phone", q.CompanyPhone);
        cmd.AddParam("@company_tel", q.CompanyTel);
        cmd.AddParam("@company_email", q.CompanyEmail);
        cmd.AddParam("@customer_name", q.CustomerName);
        cmd.AddParam("@customer_contact", q.CustomerContact);
        cmd.AddParam("@customer_phone", q.CustomerPhone);
        cmd.AddParam("@customer_email", q.CustomerEmail);
        cmd.AddParam("@quote_date", q.QuoteDate);
        cmd.AddParam("@total_amount", q.TotalAmount);
        cmd.AddParam("@calculation_info", q.CalculationInfo);
        cmd.AddParam("@status", q.Status);
        cmd.AddParam("@created_by", q.CreatedBy);
        cmd.AddParam("@created_at", q.CreatedAt);
        cmd.AddParam("@updated_at", q.UpdatedAt);
        cmd.AddParam("@currency", q.Currency);
        cmd.AddParam("@validity", q.Validity);
        cmd.AddParam("@payment", q.Payment);
        cmd.AddParam("@delivery_time", q.DeliveryTime);
        cmd.AddParam("@delivery_method", q.DeliveryMethod);
        cmd.AddParam("@filename", q.Filename);
    }
}
