using Quotix.Common;
using Quotix.Models;
using Quotix.Repositories;

namespace Quotix.Services;

/// <summary>
/// 报价单业务逻辑层 — 编号生成 / 金额计算 / 数据编排
/// SQL 访问已下沉到 QuotationRepository
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

    public List<Quotation> GetQuotations() => _repo.GetAll(Constants.LocalUserId);

    public List<Quotation> GetAllQuotationsWithItems() => _repo.GetAllWithItems();

    public Quotation? GetQuotation(string id) => _repo.GetById(id);

    // ============ 创建 ============

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

        for (int i = 0; i < q.Items.Count; i++)
        {
            var item = q.Items[i];
            item.Id = IdGenerator.New();
            item.QuotationId = q.Id;
            item.SortOrder = i;
        }

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

    public void UpdateQuotation(Quotation q)
    {
        q.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        q.TotalAmount = q.Items.Sum(i => i.TotalPrice);

        for (int i = 0; i < q.Items.Count; i++)
        {
            var item = q.Items[i];
            item.Id = IdGenerator.New();
            item.QuotationId = q.Id;
            item.SortOrder = i;
        }

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

    public void DeleteQuotation(string id) => _repo.Delete(id);

    // ============ 工具方法 ============

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
}
