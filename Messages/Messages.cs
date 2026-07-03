using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Quotix.Models;

namespace Quotix.Messages;

/// <summary>
/// 更新可用消息，通知 MainWindow 显示更新徽章。
/// </summary>
/// <param name="hasUpdate">是否有可用更新</param>
public sealed class UpdateAvailableMessage : ValueChangedMessage<bool>
{
    public UpdateAvailableMessage(bool hasUpdate) : base(hasUpdate) { }
}

/// <summary>
/// 主题变化消息。
/// </summary>
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
/// 编辑报价消息。
/// </summary>
public sealed class EditQuotationMessage : ValueChangedMessage<Quotation>
{
    public EditQuotationMessage(Quotation quotation) : base(quotation) { }
}

/// <summary>
/// 进度条状态消息（设置页 → MainWindow 遮罩）。
/// </summary>
public record ProgressState(bool IsVisible, double Percentage, string Text);

/// <summary>
/// 进度条状态消息，携带进度状态信息。
/// </summary>
public sealed class ProgressMessage : ValueChangedMessage<ProgressState>
{
    public ProgressMessage(ProgressState state) : base(state) { }
}
