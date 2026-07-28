using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Quotix.Services;

/// <summary>
/// 管理系统托盘图标及窗口恢复、退出操作。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly AppSettingsService _settingsService;
    private Forms.NotifyIcon? _notifyIcon;
    private Icon? _icon;
    private Action? _restoreAction;
    private Action? _exitAction;

    public TrayIconService(AppSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void Initialize(Action restoreAction, Action exitAction)
    {
        if (_notifyIcon != null)
            return;

        _restoreAction = restoreAction;
        _exitAction = exitAction;
        _icon = LoadApplicationIcon();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 Quotix", null, (_, _) => InvokeOnUi(_restoreAction));
        menu.Items.Add("打开导出文件夹", null, (_, _) => OpenExportFolder());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => InvokeOnUi(_exitAction));

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "Quotix",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.MouseClick += OnMouseClick;
    }

    private void OnMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
            InvokeOnUi(_restoreAction);
    }

    private static void InvokeOnUi(Action? action)
    {
        if (action == null)
            return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    private void OpenExportFolder()
    {
        var path = _settingsService.GetDefaultExportPath();
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static Icon LoadApplicationIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "app.ico");
        if (File.Exists(iconPath))
            return new Icon(iconPath);

        return Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
            ?? (Icon)SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.MouseClick -= OnMouseClick;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _icon?.Dispose();
        _icon = null;
        _restoreAction = null;
        _exitAction = null;
    }
}
