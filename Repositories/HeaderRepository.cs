using Microsoft.Data.Sqlite;
using Quotix.Common;
using Quotix.Models;

namespace Quotix.Repositories;

/// <summary>
/// 收录信息数据访问层 — 纯 SQL，继承 BaseRepository
/// </summary>
public class HeaderRepository : BaseRepository
{
    public HeaderRepository(DatabaseProvider db) : base(db) { }

    // ============ Owners ============

    public List<Owner> GetOwners() =>
        Query("SELECT * FROM owners ORDER BY name", null, ReadOwner);

    public Owner InsertOwner(Owner owner)
    {
        owner.Id = IdGenerator.New();
        Execute("INSERT INTO owners (id, name, phone, tel, email) VALUES (@id, @name, @phone, @tel, @email)",
            new() { ["@id"] = owner.Id, ["@name"] = owner.Name, ["@phone"] = owner.Phone, ["@tel"] = owner.Tel, ["@email"] = owner.Email });
        return owner;
    }

    public void UpdateOwner(Owner owner) =>
        Execute("UPDATE owners SET name=@name, phone=@phone, tel=@tel, email=@email WHERE id=@id",
            new() { ["@id"] = owner.Id, ["@name"] = owner.Name, ["@phone"] = owner.Phone, ["@tel"] = owner.Tel, ["@email"] = owner.Email });

    public void DeleteOwner(string id) =>
        Execute("DELETE FROM owners WHERE id = @id", new() { ["@id"] = id });

    public bool OwnerExists(SqliteConnection conn, SqliteTransaction tx, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM owners WHERE id = @id";
        cmd.AddParam("@id", id);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    public void InsertOwnerTx(SqliteConnection conn, SqliteTransaction tx, Owner owner)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO owners (id, name, phone, tel, email) VALUES (@id, @name, @phone, @tel, @email)";
        cmd.AddParam("@id", owner.Id);
        cmd.AddParam("@name", owner.Name);
        cmd.AddParam("@phone", owner.Phone);
        cmd.AddParam("@tel", owner.Tel);
        cmd.AddParam("@email", owner.Email);
        cmd.ExecuteNonQuery();
    }

    // ============ Customers ============

    public List<Customer> GetCustomers() =>
        Query("SELECT * FROM customers ORDER BY company_name", null, ReadCustomer);

    public Customer InsertCustomer(Customer customer)
    {
        customer.Id = IdGenerator.New();
        Execute("INSERT INTO customers (id, company_name, contact, phone, email) VALUES (@id, @company_name, @contact, @phone, @email)",
            new() { ["@id"] = customer.Id, ["@company_name"] = customer.CompanyName, ["@contact"] = customer.Contact, ["@phone"] = customer.Phone, ["@email"] = customer.Email });
        return customer;
    }

    public void UpdateCustomer(Customer customer) =>
        Execute("UPDATE customers SET company_name=@company_name, contact=@contact, phone=@phone, email=@email WHERE id=@id",
            new() { ["@id"] = customer.Id, ["@company_name"] = customer.CompanyName, ["@contact"] = customer.Contact, ["@phone"] = customer.Phone, ["@email"] = customer.Email });

    public void DeleteCustomer(string id) =>
        Execute("DELETE FROM customers WHERE id = @id", new() { ["@id"] = id });

    public bool CustomerExists(SqliteConnection conn, SqliteTransaction tx, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM customers WHERE id = @id";
        cmd.AddParam("@id", id);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    public void InsertCustomerTx(SqliteConnection conn, SqliteTransaction tx, Customer customer)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO customers (id, company_name, contact, phone, email) VALUES (@id, @company_name, @contact, @phone, @email)";
        cmd.AddParam("@id", customer.Id);
        cmd.AddParam("@company_name", customer.CompanyName);
        cmd.AddParam("@contact", customer.Contact);
        cmd.AddParam("@phone", customer.Phone);
        cmd.AddParam("@email", customer.Email);
        cmd.ExecuteNonQuery();
    }

    public void Clear(string tableName)
    {
        Execute($"DELETE FROM {(tableName == "owners" ? "owners" : "customers")}", new());
        // 全表清空后回收磁盘空间
        Db.Vacuum();
    }

    // ============ Reader ============

    public static Owner ReadOwner(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        Name = r.GetString(r.GetOrdinal("name")),
        Phone = r.GetSafeString("phone"),
        Tel = r.GetSafeString("tel"),
        Email = r.GetSafeString("email")
    };

    public static Customer ReadCustomer(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        CompanyName = r.GetString(r.GetOrdinal("company_name")),
        Contact = r.GetSafeString("contact"),
        Phone = r.GetSafeString("phone"),
        Email = r.GetSafeString("email")
    };
}
