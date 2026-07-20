using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Quotix.Messages;

/// <summary>
/// 主题切换消息，携带当前是否为深色模式的信息。
/// </summary>
public sealed class ThemeChangedMessage : ValueChangedMessage<bool>
{
    /// <summary>
    /// 初始化 ThemeChangedMessage 实例。
    /// </summary>
    /// <param name="isDark">是否为深色模式</param>
    public ThemeChangedMessage(bool isDark) : base(isDark) { }
}

/// <summary>
/// 编辑报价单消息，携带要编辑的报价单 ID。
/// </summary>
public sealed class EditQuotationMessage : ValueChangedMessage<string>
{
    /// <summary>
    /// 初始化 EditQuotationMessage 实例。
    /// </summary>
    /// <param name="quotationId">报价单 ID</param>
    public EditQuotationMessage(string quotationId) : base(quotationId) { }
}

/// <summary>
/// 关于对话框请求消息。
/// </summary>
public sealed class AboutRequestedMessage : RequestMessage<bool> { }

/// <summary>
/// 打开标签页请求消息，携带要打开的标签页 ID。
/// </summary>
public sealed class OpenTabMessage : ValueChangedMessage<string>
{
    /// <summary>
    /// 初始化 OpenTabMessage 实例。
    /// </summary>
    /// <param name="tabId">标签页 ID</param>
    public OpenTabMessage(string tabId) : base(tabId) { }
}

/// <summary>
/// 进度条状态消息（设置页 → MainWindow 遮罩）。
/// </summary>
/// <param name="IsVisible">是否显示进度条</param>
/// <param name="Percentage">进度百分比</param>
/// <param name="Text">显示文本</param>
public record ProgressState(bool IsVisible, double Percentage, string Text);

/// <summary>
/// 进度条状态消息，携带进度状态信息。
/// </summary>
public sealed class ProgressMessage : ValueChangedMessage<ProgressState>
{
    /// <summary>
    /// 初始化 ProgressMessage 实例。
    /// </summary>
    /// <param name="state">进度状态</param>
    public ProgressMessage(ProgressState state) : base(state) { }
}

/// <summary>
/// 显示更新弹窗请求消息。
/// </summary>
public sealed class ShowUpdateOverlayMessage : RequestMessage<bool> { }

/// <summary>
/// 快捷输入启用状态变更消息，携带当前是否启用快捷输入。
/// </summary>
public sealed class QuickInputEnabledChangedMessage : ValueChangedMessage<bool>
{
    /// <summary>
    /// 初始化 QuickInputEnabledChangedMessage 实例。
    /// </summary>
    /// <param name="enabled">是否启用快捷输入</param>
    public QuickInputEnabledChangedMessage(bool enabled) : base(enabled) { }
}

/// <summary>
/// 全局模糊搜索开关变更消息，携带当前是否启用模糊搜索。
/// 用于同步报价单页快捷搜索的匹配算法（高级分散匹配 / 基础匹配）。
/// </summary>
public sealed class QuickInputFuzzyChangedMessage : ValueChangedMessage<bool>
{
    /// <summary>
    /// 初始化 QuickInputFuzzyChangedMessage 实例。
    /// </summary>
    /// <param name="enabled">是否启用全局模糊搜索</param>
    public QuickInputFuzzyChangedMessage(bool enabled) : base(enabled) { }
}

/// <summary>
/// 快捷输入字段映射变更消息，携带当前变更的数据库类型（NDT / RVI）。
/// 用于通知报价单页清理快速输入缓存并立即使用最新映射。
/// </summary>
public sealed class QuickInputMappingChangedMessage : ValueChangedMessage<string>
{
    /// <summary>
    /// 初始化 QuickInputMappingChangedMessage 实例。
    /// </summary>
    /// <param name="dbType">发生映射变更的数据库类型</param>
    public QuickInputMappingChangedMessage(string dbType) : base(dbType) { }
}

/// <summary>
/// 产品数据变更消息，携带发生变更的表名（如 products_ndt / products_rvi_change）。
/// 用于在产品导入、清空后，通知设置页快捷输入卡片刷新可用的表头下拉项。
/// </summary>
public sealed class ProductDataChangedMessage : ValueChangedMessage<string>
{
    /// <summary>
    /// 初始化 ProductDataChangedMessage 实例。
    /// </summary>
    /// <param name="tableName">发生数据变更的表名</param>
    public ProductDataChangedMessage(string tableName) : base(tableName) { }
}
