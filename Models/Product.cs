namespace Quotix.Models;

/// <summary>产品模型 — DataJson 存储 JSON 字符串，支持动态列结构</summary>
public class Product
{
    /// <summary>产品唯一标识</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>所属数据表名称</summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>产品数据 JSON（动态列结构序列化字符串）</summary>
    public string DataJson { get; set; } = "{}";

    /// <summary>创建者用户标识</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>创建时间</summary>
    public string? CreatedAt { get; set; }

    /// <summary>更新时间</summary>
    public string? UpdatedAt { get; set; }
}

/// <summary>负责人模型（报价方联系信息）</summary>
public class Owner
{
    /// <summary>负责人唯一标识</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>负责人姓名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>手机号码</summary>
    public string? Phone { get; set; }

    /// <summary>固定电话</summary>
    public string? Tel { get; set; }

    /// <summary>电子邮箱</summary>
    public string? Email { get; set; }
}

/// <summary>客户模型（需求方公司信息）</summary>
public class Customer
{
    /// <summary>客户唯一标识</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>公司名称</summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>联系人姓名</summary>
    public string? Contact { get; set; }

    /// <summary>联系电话</summary>
    public string? Phone { get; set; }

    /// <summary>电子邮箱</summary>
    public string? Email { get; set; }
}
