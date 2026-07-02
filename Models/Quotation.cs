namespace Quotix.Models;

/// <summary>报价单主模型</summary>
public class Quotation
{
    /// <summary>报价单唯一标识</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>报价单编号（如 CDC20260115）</summary>
    public string? QuoteNumber { get; set; }

    /// <summary>负责人姓名</summary>
    public string? CompanyContact { get; set; }

    /// <summary>负责人电话</summary>
    public string? CompanyPhone { get; set; }

    /// <summary>负责人固话</summary>
    public string? CompanyTel { get; set; }

    /// <summary>负责人邮箱</summary>
    public string? CompanyEmail { get; set; }

    /// <summary>客户公司名称</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>客户联系人</summary>
    public string? CustomerContact { get; set; }

    /// <summary>客户电话</summary>
    public string? CustomerPhone { get; set; }

    /// <summary>客户邮箱</summary>
    public string? CustomerEmail { get; set; }

    /// <summary>报价日期（如 "2026年1月15日"）</summary>
    public string? QuoteDate { get; set; }

    /// <summary>总金额</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>计价信息（JSON 字符串）</summary>
    public string? CalculationInfo { get; set; }

    /// <summary>状态（draft / sent / accepted）</summary>
    public string Status { get; set; } = "draft";

    /// <summary>创建者用户标识</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>创建时间</summary>
    public string? CreatedAt { get; set; }

    /// <summary>更新时间</summary>
    public string? UpdatedAt { get; set; }

    /// <summary>货币单位</summary>
    public string? Currency { get; set; }

    /// <summary>报价有效期</summary>
    public string? Validity { get; set; }

    /// <summary>付款方式</summary>
    public string? Payment { get; set; }

    /// <summary>交货时间</summary>
    public string? DeliveryTime { get; set; }

    /// <summary>交货方式</summary>
    public string? DeliveryMethod { get; set; }

    /// <summary>关联 Excel 文件名</summary>
    public string? Filename { get; set; }

    /// <summary>报价单明细项列表</summary>
    public List<QuotationItem> Items { get; set; } = new();
}

/// <summary>报价单明细项模型</summary>
public class QuotationItem
{
    /// <summary>明细项唯一标识</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>所属报价单 ID</summary>
    public string QuotationId { get; set; } = string.Empty;

    /// <summary>产品名称</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>产品编码</summary>
    public string? Code { get; set; }

    /// <summary>产品描述</summary>
    public string? Description { get; set; }

    /// <summary>数量</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>单价</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>总价（Quantity × UnitPrice）</summary>
    public decimal TotalPrice { get; set; }

    /// <summary>原价（文本格式，用于显示）</summary>
    public string? OriginalPrice { get; set; }

    /// <summary>排序号（用于明细项排序）</summary>
    public int SortOrder { get; set; }
}
