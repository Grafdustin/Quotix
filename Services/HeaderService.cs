using Quotix.Models;
using Quotix.Repositories;

namespace Quotix.Services;

/// <summary>
/// 收录信息业务逻辑层 — CRUD（导入/导出已拆分到 HeaderImportService / HeaderExportService）
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

    // ============ Owners ============

    public List<Owner> GetOwners() => _cache.GetOwners();

    public Owner AddOwner(Owner owner)
    {
        var result = _repo.InsertOwner(owner);
        _cache.InvalidateHeaders();
        return result;
    }

    public void UpdateOwner(Owner owner)
    {
        _repo.UpdateOwner(owner);
        _cache.InvalidateHeaders();
    }

    public void DeleteOwner(string id)
    {
        _repo.DeleteOwner(id);
        _cache.InvalidateHeaders();
    }

    // ============ Customers ============

    public List<Customer> GetCustomers() => _cache.GetCustomers();

    public Customer AddCustomer(Customer customer)
    {
        var result = _repo.InsertCustomer(customer);
        _cache.InvalidateHeaders();
        return result;
    }

    public void UpdateCustomer(Customer customer)
    {
        _repo.UpdateCustomer(customer);
        _cache.InvalidateHeaders();
    }

    public void DeleteCustomer(string id)
    {
        _repo.DeleteCustomer(id);
        _cache.InvalidateHeaders();
    }

    public void ClearAll(string tableName)
    {
        _repo.Clear(tableName);
        _cache.InvalidateHeaders();
    }

    // ============ 批量导入（用于备份恢复）============

    public (int owners, int customers) ImportHeaderData(FullBackupData backup)
    {
        var result = ImportOwnersAndCustomers(backup.Owners, backup.Customers);
        if (result.owners > 0 || result.customers > 0) _cache.InvalidateHeaders();
        return result;
    }

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
