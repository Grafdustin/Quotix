using System.Windows;
using Wpf.Ui.Controls;
using WpfMessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace Quotix.Services;

/// <summary>
/// Fluent-styled dialog service — uses an in-app overlay card instead of
/// a separate OS <see cref="Window"/> to avoid WPF window-sizing quirks.
/// </summary>
public class DialogService
{
    public void ShowInfo(string message, string title = "提示")
        => Show(title, message, SymbolRegular.CheckmarkCircle16, "确定", null);

    public void ShowWarning(string message, string title = "提示")
        => Show(title, message, SymbolRegular.Warning16, "确定", null);

    public void ShowError(string message, string title = "提示")
        => Show(title, message, SymbolRegular.ErrorCircle16, "确定", null);

    public bool ShowConfirm(string message, string title = "提示")
        => Show(title, message, SymbolRegular.QuestionCircle16, "确定", "取消") == WpfMessageBoxResult.Primary;

    /// <summary>
    /// 程序内嵌密码输入弹窗 — 返回用户输入的密码，取消时返回 null。
    /// </summary>
    public string? ShowPasswordPrompt(string title, string message, string? errorMessage = null)
    {
        var mainWindow = Application.Current?.MainWindow as MainWindow;
        return mainWindow?.ShowInlinePasswordPrompt(title, message, errorMessage);
    }

    // ── core ──

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
