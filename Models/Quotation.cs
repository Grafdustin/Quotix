namespace Quotix.Models;

public class Quotation
{
    public string Id { get; set; } = string.Empty;
    public string? QuoteNumber { get; set; }
    public string? CompanyContact { get; set; }
    public string? CompanyPhone { get; set; }
    public string? CompanyTel { get; set; }
    public string? CompanyEmail { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerContact { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
    public string? QuoteDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string? CalculationInfo { get; set; }
    public string Status { get; set; } = "draft";
    public string CreatedBy { get; set; } = string.Empty;
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
    public string? Currency { get; set; }
    public string? Validity { get; set; }
    public string? Payment { get; set; }
    public string? DeliveryTime { get; set; }
    public string? DeliveryMethod { get; set; }
    public string? Filename { get; set; }
    public List<QuotationItem> Items { get; set; } = new();
}

public class QuotationItem
{
    public string Id { get; set; } = string.Empty;
    public string QuotationId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Description { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public string? OriginalPrice { get; set; }
    public int SortOrder { get; set; }
}
