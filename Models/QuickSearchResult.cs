namespace Quotix.Models;

/// <summary>
/// 快速输入搜索结果模型
/// </summary>
public class QuickSearchResult
{
    /// <summary>产品名称 / 负责人姓名 / 客户名称</summary>
    public string Title { get; set; } = "";
    
    /// <summary>产品编码 / 负责人电话 / 客户联系人</summary>
    public string Subtitle { get; set; } = "";
    
    /// <summary>单价文本（仅产品类型）</summary>
    public string PriceText { get; set; } = "";
    
    /// <summary>单价数值</summary>
    public decimal Price { get; set; }
    
    /// <summary>原始 JSON 数据（完整产品信息）</summary>
    public Dictionary<string, string>? RawData { get; set; }
    
    /// <summary>搜索类型: product / owner / customer</summary>
    public string ResultType { get; set; } = "product";
}
