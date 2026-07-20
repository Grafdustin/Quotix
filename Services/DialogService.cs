using System.Windows;
using Wpf.Ui.Controls;
using WpfMessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace Quotix.Services;

/// <summary>
/// 对话框服务。
/// 使用应用内嵌浮层弹窗（非独立 Window），避免 WPF 窗口尺寸异常问题。
/// </summary>
public class DialogService
{
    /// <summary>显示信息提示弹窗</summary>
    public void ShowInfo(string message, string title = "提示")
        => Show(title, message, SymbolRegular.CheckmarkCircle16, "确定", null);

    /// <summary>显示警告提示弹窗</summary>
    public void ShowWarning(string message, string title = "提示")
        => Show(title, message, SymbolRegular.Warning16, "确定", null);

    /// <summary>显示错误提示弹窗</summary>
    public void ShowError(string message, string title = "提示")
        => Show(title, message, SymbolRegular.ErrorCircle16, "确定", null);

    /// <summary>显示确认弹窗，返回用户是否确认</summary>
    public bool ShowConfirm(string message, string title = "提示")
        => Show(title, message, SymbolRegular.QuestionCircle16, "确定", "取消") == WpfMessageBoxResult.Primary;

    /// <summary>显示输入弹窗，确认时返回文本，取消时返回 null。</summary>
    public string? ShowInput(string message, string initialValue, string title = "命名报价单")
    {
        var mainWindow = Application.Current?.MainWindow as MainWindow;
        return mainWindow?.ShowInlineInputDialog(
            title,
            message,
            initialValue,
            SymbolRegular.Edit20,
            "保存",
            "取消");
    }

    // ============ 私有方法 ============

    /// <summary>通过 MainWindow 显示内嵌弹窗</summary>
    private static WpfMessageBoxResult Show(
        string title,
        string message,
        SymbolRegular icon,
        string primaryText,
        string? cancelText)
    {
        var mainWindow = Application.Current?.MainWindow as MainWindow;
        if (mainWindow == null)
            return WpfMessageBoxResult.None;

        var confirmed = mainWindow.ShowInlineDialog(title, message, icon, primaryText, cancelText);
        return confirmed ? WpfMessageBoxResult.Primary : WpfMessageBoxResult.None;
    }
}
