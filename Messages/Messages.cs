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
/// 更新可用消息，携带是否有新版本的信息。
/// </summary>
public sealed class UpdateAvailableMessage : ValueChangedMessage<bool>
{
    /// <summary>
    /// 初始化 UpdateAvailableMessage 实例。
    /// </summary>
    /// <param name="hasUpdate">是否有新版本可用</param>
    public UpdateAvailableMessage(bool hasUpdate) : base(hasUpdate) { }
}
