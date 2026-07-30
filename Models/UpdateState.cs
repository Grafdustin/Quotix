using CommunityToolkit.Mvvm.ComponentModel;

namespace Quotix.Models;

/// <summary>
/// 更新检查阶段枚举，驱动 UI 状态切换。
/// </summary>
public enum UpdateStage
{
    /// <summary>初始空闲</summary>
    Idle,

    /// <summary>正在检查更新</summary>
    Checking,

    /// <summary>发现新版本 — 弹窗显示详情</summary>
    UpdateAvailable,

    /// <summary>已是最新版本</summary>
    UpToDate,

    /// <summary>失败 — 显示「重试」</summary>
    Failed
}

/// <summary>
/// 统一更新状态对象。
/// UI 只绑定此对象的属性，状态变化自动驱动 UI。
/// </summary>
public partial class UpdateState : ObservableObject
{
    // ─── 核心状态 ───

    /// <summary>当前流水线阶段</summary>
    [ObservableProperty] private UpdateStage _stage = UpdateStage.Idle;

    // ─── 信息字段 ───

    /// <summary>当前状态提示文本</summary>
    [ObservableProperty] private string _message = "正在检查更新...";

    /// <summary>错误信息</summary>
    [ObservableProperty] private string _error = "";

    /// <summary>检测到的新版本号</summary>
    [ObservableProperty] private string _newVersion = "";

    /// <summary>当前版本号</summary>
    [ObservableProperty] private string _currentVersion = "";

    /// <summary>更新包文件大小（字节）</summary>
    [ObservableProperty] private long _fileSize;

    /// <summary>发布日期</summary>
    [ObservableProperty] private string _releaseDate = "";

    /// <summary>更新日志条目（支持章节头部）</summary>
    [ObservableProperty] private ChangelogEntry[] _changelog = Array.Empty<ChangelogEntry>();

    // ─── UI 计算属性 ───

    /// <summary>左边按钮文本</summary>
    public string LeftButtonText => "稍后";

    /// <summary>按钮文本</summary>
    public string ActionButtonText => Stage switch
    {
        UpdateStage.UpdateAvailable => "立即更新",
        UpdateStage.Failed          => "重试",
        _                           => "检查更新"
    };

    /// <summary>文件大小显示文本</summary>
    public string FileSizeDisplay => FileSize > 0 ? FormatSize(FileSize) : "";

    // ─── 属性变更联动 ───

    partial void OnStageChanged(UpdateStage value)
    {
        OnPropertyChanged(nameof(ActionButtonText));
        OnPropertyChanged(nameof(LeftButtonText));
    }

    partial void OnFileSizeChanged(long value)
        => OnPropertyChanged(nameof(FileSizeDisplay));

    // ─── 格式化工具 ───

    /// <summary>格式化字节数为人类可读大小</summary>
    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_048_576)
            return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

}

/// <summary>
/// 更新日志条目 — 支持章节头部（# 开头）和普通内容。
/// </summary>
public class ChangelogEntry
{
    /// <summary>是否为章节头部（对应 latest.yml 中 # 开头的行）</summary>
    public bool IsHeader { get; set; }

    /// <summary>条目文本</summary>
    public string Text { get; set; } = "";
}
