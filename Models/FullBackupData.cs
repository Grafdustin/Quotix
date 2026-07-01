using System.Text.Json.Serialization;

namespace Quotix.Models;

public record FullBackupData
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("exported_at")]
    public string ExportedAt { get; init; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    [JsonPropertyName("products")]
    public List<Product> Products { get; init; } = [];

    [JsonPropertyName("quotations")]
    public List<Quotation> Quotations { get; init; } = [];

    [JsonPropertyName("owners")]
    public List<Owner> Owners { get; init; } = [];

    [JsonPropertyName("customers")]
    public List<Customer> Customers { get; init; } = [];
}
