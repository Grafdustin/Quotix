using Quotix.Common;
using Quotix.Models;
using Quotix.Repositories;

namespace Quotix.Services;

/// <summary>
/// 报价单业务逻辑层。
/// 负责编号生成、金额计算、数据编排及事务管理。
/// 数据库访问委托给 QuotationRepository。
/// </summary>
public class QuotationService
{
    private readonly DatabaseProvider _db;
    private readonly QuotationRepository _repo;

    public QuotationService(DatabaseProvider db, QuotationRepository repo)
    {
        _db = db;
        _repo = repo;
    }

    // ============ 查询（委托 Repository）============

    /// <summary>获取当前用户的所有报价单</summary>
    public List<Quotation> GetQuotations()
    {
        var quotations = _repo.GetAll(Constants.LocalUserId);
        foreach (var q in quotations)
            EnsureQuoteNumber(q);
        return quotations;
    }

    /// <summary>获取所有报价单及其明细项</summary>
    public List<Quotation> GetAllQuotationsWithItems()
    {
        var quotations = _repo.GetAllWithItems();
        foreach (var q in quotations)
            EnsureQuoteNumber(q);
        return quotations;
    }

    /// <summary>根据 ID 获取报价单（含明细项）</summary>
    public Quotation? GetQuotation(string id)
    {
        var quotation = _repo.GetById(id);
        if (quotation != null)
            EnsureQuoteNumber(quotation);
        return quotation;
    }

    // ============ 创建 ============

    /// <summary>创建新报价单，自动生成 ID、编号和时间</summary>
    public Quotation CreateQuotation(Quotation q)
    {
        q.Id = IdGenerator.New();
        q.CreatedBy = Constants.LocalUserId;
        q.QuoteNumber = GenerateQuoteNumber(q.QuoteDate);

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        q.CreatedAt = now;
        q.UpdatedAt = now;
        q.Status = "draft";
        q.TotalAmount = q.Items.Sum(i => i.TotalPrice);

        // 为每个明细项生成 ID 和排序号
        for (int i = 0; i < q.Items.Count; i++)
        {
            var item = q.Items[i];
            item.Id = IdGenerator.New();
            item.QuotationId = q.Id;
            item.SortOrder = i;
        }

        // 在事务中插入报价单主记录和所有明细项
        using var conn = _db.GetConnection();
        using var tx = conn.BeginTransaction();

        try
        {
            _repo.InsertQuotation(conn, tx, q);
            foreach (var item in q.Items)
                _repo.InsertItem(conn, tx, item);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        return q;
    }

    /// <summary>从备份数据创建报价单（跳过编号生成）</summary>
    public void CreateQuotationFromBackup(Quotation q)
    {
        using var conn = _db.GetConnection();
        using var tx = conn.BeginTransaction();

        try
        {
            _repo.InsertQuotation(conn, tx, q);
            if (q.Items != null)
            {
                foreach (var item in q.Items)
                    _repo.InsertItem(conn, tx, item);
            }
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ============ 更新 ============

    /// <summary>更新报价单，按最新报价日期更新编号，并替换所有明细项</summary>
    public void UpdateQuotation(Quotation q)
    {
        q.QuoteNumber = GenerateQuoteNumber(q.QuoteDate);

        q.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        q.TotalAmount = q.Items.Sum(i => i.TotalPrice);

        // 重新生成所有明细项的 ID 和排序号
        for (int i = 0; i < q.Items.Count; i++)
        {
            var item = q.Items[i];
            item.Id = IdGenerator.New();
            item.QuotationId = q.Id;
            item.SortOrder = i;
        }

        // 在事务中删除旧明细 → 更新主记录 → 插入新明细
        using var conn = _db.GetConnection();
        using var tx = conn.BeginTransaction();

        try
        {
            _repo.DeleteItems(conn, tx, q.Id);
            _repo.UpdateQuotation(conn, tx, q);
            foreach (var item in q.Items)
                _repo.InsertItem(conn, tx, item);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ============ 删除 ============

    /// <summary>根据 ID 删除报价单（级联删除明细项）</summary>
    public void DeleteQuotation(string id) => _repo.Delete(id);

    // ============ 工具方法 ============

    /// <summary>
    /// 生成报价单编号。
    /// 格式：CDC + 日期（如 CDC20260115）。
    /// 尝试从中文日期字符串解析，失败则使用当前日期。
    /// </summary>
    public static string GenerateQuoteNumber(string? quoteDate)
    {
        DateTime date;
        if (!string.IsNullOrEmpty(quoteDate))
        {
            var match = System.Text.RegularExpressions.Regex.Match(quoteDate, @"(\d+)年(\d+)月(\d+)日");
            if (match.Success)
                date = new DateTime(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
            else
                date = DateTime.Now;
        }
        else
        {
            date = DateTime.Now;
        }
        return $"CDC{date:yyyyMMdd}";
    }

    /// <summary>旧数据若缺少报价编号，则按报价日期补齐，避免历史导出空编号。</summary>
    private static void EnsureQuoteNumber(Quotation q)
    {
        if (string.IsNullOrWhiteSpace(q.QuoteNumber))
            q.QuoteNumber = GenerateQuoteNumber(q.QuoteDate);
    }
}
