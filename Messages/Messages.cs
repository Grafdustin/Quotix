using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Quotix.Messages;

/// <summary>主题切换消息</summary>
public sealed class ThemeChangedMessage : ValueChangedMessage<bool>
{
    public ThemeChangedMessage(bool isDark) : base(isDark) { }
}

/// <summary>编辑报价单消息</summary>
public sealed class EditQuotationMessage : ValueChangedMessage<string>
{
    public EditQuotationMessage(string quotationId) : base(quotationId) { }
}

/// <summary>关于对话框请求消息</summary>
public sealed class AboutRequestedMessage : RequestMessage<bool> { }

/// <summary>打开标签页请求</summary>
public sealed class OpenTabMessage : ValueChangedMessage<string>
{
    public OpenTabMessage(string tabId) : base(tabId) { }
}

/// <summary>进度条状态消息（设置页 → MainWindow 遮罩）</summary>
public record ProgressState(bool IsVisible, double Percentage, string Text);

public sealed class ProgressMessage : ValueChangedMessage<ProgressState>
{
    public ProgressMessage(ProgressState state) : base(state) { }
}
