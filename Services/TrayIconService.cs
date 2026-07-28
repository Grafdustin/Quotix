using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using Forms = System.Windows.Forms;

namespace Quotix.Services;

/// <summary>
/// 管理系统托盘图标及窗口恢复、退出操作。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly AppSettingsService _settingsService;
    private Forms.NotifyIcon? _notifyIcon;
    private ContextMenu? _contextMenu;
    private Icon? _icon;
    private Action? _restoreAction;
    private Action? _exitAction;
    private volatile bool _isContextMenuOpen;
    private bool _suppressNextRightClick;

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

        _contextMenu = CreateContextMenu();

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "Quotix",
            Visible = true
        };
        _notifyIcon.MouseDown += OnMouseDown;
        _notifyIcon.MouseClick += OnMouseClick;
    }

    private void OnMouseDown(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button != Forms.MouseButtons.Right || !_isContextMenuOpen)
            return;

        _suppressNextRightClick = true;
        InvokeOnUi(() =>
        {
            if (_contextMenu != null)
                _contextMenu.IsOpen = false;
        });
    }

    private void OnMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
            InvokeOnUi(_restoreAction);
        else if (e.Button == Forms.MouseButtons.Right)
        {
            if (_suppressNextRightClick)
            {
                _suppressNextRightClick = false;
                return;
            }

            if (GetCursorPos(out var anchor))
                ShowContextMenu(anchor);
        }
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu
        {
            MinWidth = 184,
            Placement = PlacementMode.MousePoint,
            StaysOpen = false,
            Focusable = true
        };
        menu.Opened += (_, _) => _isContextMenuOpen = true;
        menu.Closed += (_, _) => _isContextMenuOpen = false;

        var openItem = new Wpf.Ui.Controls.MenuItem
        {
            Header = "打开 Quotix",
            Icon = new SymbolIcon(SymbolRegular.Window20, 16, false)
        };
        openItem.Click += (_, _) => InvokeOnUi(_restoreAction);

        var exportItem = new Wpf.Ui.Controls.MenuItem
        {
            Header = "打开导出文件夹",
            Icon = new SymbolIcon(SymbolRegular.FolderOpen20, 16, false)
        };
        exportItem.Click += (_, _) => OpenExportFolder();

        var exitItem = new Wpf.Ui.Controls.MenuItem
        {
            Header = "退出",
            Icon = new SymbolIcon(SymbolRegular.Power20, 16, false)
        };
        exitItem.Click += (_, _) => InvokeOnUi(_exitAction);

        menu.Items.Add(openItem);
        menu.Items.Add(exportItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        return menu;
    }

    private void ShowContextMenu(NativePoint anchor)
    {
        InvokeOnUi(() =>
        {
            if (_contextMenu == null)
                return;

            if (_contextMenu.IsOpen)
            {
                _contextMenu.IsOpen = false;
                return;
            }

            _contextMenu.Placement = PlacementMode.MousePoint;
            _contextMenu.HorizontalOffset = 0;
            _contextMenu.VerticalOffset = 0;
            _contextMenu.Opacity = 0;
            _contextMenu.IsOpen = true;

            _contextMenu.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
            {
                if (_contextMenu?.IsOpen != true)
                    return;

                _contextMenu.Focus();
                if (PresentationSource.FromVisual(_contextMenu) is HwndSource source)
                    PositionMenu(_contextMenu, source, anchor);

                _contextMenu.Opacity = 1;
            });
        });
    }

    private static void PositionMenu(ContextMenu menu, HwndSource source, NativePoint anchor)
    {
        var transform = source.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var cursor = transform.Transform(new System.Windows.Point(anchor.X, anchor.Y));

        menu.Placement = PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = cursor.X;
        menu.VerticalOffset = cursor.Y - menu.ActualHeight;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
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
        if (_contextMenu != null)
        {
            _contextMenu.IsOpen = false;
            _contextMenu.Items.Clear();
            _contextMenu = null;
        }

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.MouseDown -= OnMouseDown;
            _notifyIcon.MouseClick -= OnMouseClick;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _icon?.Dispose();
        _icon = null;
        _restoreAction = null;
        _exitAction = null;
    }
}
