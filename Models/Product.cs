namespace Quotix.Models;

/// <summary>
/// 产品模型 — DataJson 存 JSON 字符串，支持动态列结构
/// </summary>
public class Product
{
    public string Id { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string DataJson { get; set; } = "{}";
    public string CreatedBy { get; set; } = string.Empty;
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
}

public class Owner
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Tel { get; set; }
    public string? Email { get; set; }
}

public class Customer
{
    public string Id { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
}

public record HeaderExportData(List<Owner> Owners, List<Customer> Customers);
