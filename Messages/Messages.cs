using CommunityToolkit.Mvvm.Messaging;

using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Quotix.Messages;

/// <summary>
/// 主题变更消息。
/// </summary>
/// <param name="isDarkMode">是否深色模式</param>
public sealed class ThemeChangedMessage : ValueChangedMessage<bool>
{
    public ThemeChangedMessage(bool isDarkMode) : base(isDarkMode) { }
}

/// <summary>
/// 关于对话框请求消息。
/// </summary>
public sealed class AboutRequestedMessage : RequestMessage<bool>
{
}

/// <summary>
/// 编辑报价单消息。
/// </summary>
/// <param name="quotationId">报价单 ID</param>
public sealed class EditQuotationMessage : ValueChangedMessage<string>
{
    public EditQuotationMessage(string quotationId) : base(quotationId) { }
}

/// <summary>
/// 更新可用消息，通知 MainWindow 显示更新徽章。
/// </summary>
/// <param name="hasUpdate">是否有可用更新</param>
public sealed class UpdateAvailableMessage : ValueChangedMessage<bool>
{
    public UpdateAvailableMessage(bool hasUpdate) : base(hasUpdate) { }
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
    public ProgressMessage(ProgressState state) : base(state) { }
}
