using Quotix.Common;
using Quotix.Models;
using Quotix.Repositories;

namespace Quotix.Services;

/// <summary>
/// 收录信息业务逻辑层。
/// 负责人（Owner）和客户（Customer）的 CRUD，导入/导出已拆分到独立 Service。
/// </summary>
public class HeaderService
{
    private readonly HeaderRepository _repo;
    private readonly CacheService _cache;

    public HeaderService(HeaderRepository repo, CacheService cache)
    {
        _repo = repo;
        _cache = cache;
    }

    // ============ 负责人（Owner） ============

    /// <summary>获取所有负责人（优先读缓存）</summary>
    public List<Owner> GetOwners() => _cache.GetOwners();

    /// <summary>新增负责人，同时使缓存失效</summary>
    public Owner AddOwner(Owner owner)
    {
        var result = _repo.InsertOwner(owner);
        _cache.InvalidateHeaders();
        return result;
    }

    /// <summary>更新负责人信息，同时使缓存失效</summary>
    public void UpdateOwner(Owner owner)
    {
        _repo.UpdateOwner(owner);
        _cache.InvalidateHeaders();
    }

    /// <summary>删除负责人，同时使缓存失效</summary>
    public void DeleteOwner(string id)
    {
        _repo.DeleteOwner(id);
        _cache.InvalidateHeaders();
    }

    // ============ 客户（Customer） ============

    /// <summary>获取所有客户（优先读缓存）</summary>
    public List<Customer> GetCustomers() => _cache.GetCustomers();

    /// <summary>新增客户，同时使缓存失效</summary>
    public Customer AddCustomer(Customer customer)
    {
        var result = _repo.InsertCustomer(customer);
        _cache.InvalidateHeaders();
        return result;
    }

    /// <summary>更新客户信息，同时使缓存失效</summary>
    public void UpdateCustomer(Customer customer)
    {
        _repo.UpdateCustomer(customer);
        _cache.InvalidateHeaders();
    }

    /// <summary>删除客户，同时使缓存失效</summary>
    public void DeleteCustomer(string id)
    {
        _repo.DeleteCustomer(id);
        _cache.InvalidateHeaders();
    }

    /// <summary>清空指定收录信息表，同时使缓存失效</summary>
    public void ClearAll(string tableName)
    {
        _repo.Clear(tableName);
        _cache.InvalidateHeaders();
    }

    // ============ 批量导入（用于备份恢复）============

    /// <summary>批量导入负责人和客户数据（事务内执行）</summary>
    public (int owners, int customers) ImportHeaderData(FullBackupData backup)
    {
        var result = ImportOwnersAndCustomers(backup.Owners, backup.Customers);
        if (result.owners > 0 || result.customers > 0)
            _cache.InvalidateHeaders();
        return result;
    }

    /// <summary>在事务中批量插入负责人和客户（跳过已存在的记录）</summary>
    private (int owners, int customers) ImportOwnersAndCustomers(List<Owner> owners, List<Customer> customers)
    {
        int ownerCount = 0, customerCount = 0;

        using var conn = _repo.GetConnection();
        using var tx = conn.BeginTransaction();

        try
        {
            foreach (var owner in owners)
            {
                if (string.IsNullOrWhiteSpace(owner.Name)) continue;
                if (_repo.OwnerExists(conn, tx, owner.Id)) continue;
                _repo.InsertOwnerTx(conn, tx, owner);
                ownerCount++;
            }

            foreach (var customer in customers)
            {
                if (string.IsNullOrWhiteSpace(customer.CompanyName)) continue;
                if (_repo.CustomerExists(conn, tx, customer.Id)) continue;
                _repo.InsertCustomerTx(conn, tx, customer);
                customerCount++;
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        return (ownerCount, customerCount);
    }
}
