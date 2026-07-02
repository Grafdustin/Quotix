using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Quotix.Services;

/// <summary>
/// 主题服务。
/// 管理深色/浅色模式切换，并将用户偏好持久化到设置文件。
/// </summary>
public class ThemeService
{
    private readonly AppSettingsService _settings;

    public ThemeService(AppSettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>当前是否为深色模式</summary>
    public bool IsDarkMode { get; private set; }

    /// <summary>从设置文件加载并应用主题</summary>
    public void Load()
    {
        Apply(_settings.DarkMode);
    }

    /// <summary>切换深色/浅色模式</summary>
    public void Toggle()
    {
        Apply(!IsDarkMode);
    }

    /// <summary>应用指定主题，并持久化设置</summary>
    public void Apply(bool isDark)
    {
        IsDarkMode = isDark;
        ApplicationThemeManager.Apply(
            isDark ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.None);

        _settings.DarkMode = isDark;
    }
}
