using System.Text.Json.Serialization;

namespace Quotix.Models;

/// <summary>全量备份数据模型（序列化到加密备份文件）</summary>
public record FullBackupData(List<Owner> Owners, List<Customer> Customers)
{
    /// <summary>备份格式版本号</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    /// <summary>导出时间</summary>
    [JsonPropertyName("exported_at")]
    public string ExportedAt { get; init; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>产品列表</summary>
    [JsonPropertyName("products")]
    public List<Product> Products { get; init; } = [];

    /// <summary>报价单列表</summary>
    [JsonPropertyName("quotations")]
    public List<Quotation> Quotations { get; init; } = [];

    /// <summary>负责人列表</summary>
    [JsonPropertyName("owners")]
    public List<Owner> Owners { get; init; } = [];

    /// <summary>客户列表</summary>
    [JsonPropertyName("customers")]
    public List<Customer> Customers { get; init; } = [];
}
