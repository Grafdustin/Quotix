using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Quotix.Models;

/// <summary>
/// 更新流水线阶段枚举，驱动 UI 状态切换。
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

    /// <summary>正在下载</summary>
    Downloading,

    /// <summary>下载完成 — 显示「安装」</summary>
    ReadyToInstall,

    /// <summary>正在安装</summary>
    Installing,

    /// <summary>失败 — 显示「重试」</summary>
    Failed
}

/// <summary>
/// 统一更新状态对象（单一 Progress Stream）。
/// UI 只绑定此对象的属性，状态变化自动驱动 UI。
/// </summary>
public partial class UpdateState : ObservableObject
{
    // ─── 核心状态 ───

    /// <summary>当前流水线阶段</summary>
    [ObservableProperty] private UpdateStage _stage = UpdateStage.Idle;

    /// <summary>整体进度（0-100）</summary>
    [ObservableProperty] private double _progress;

    // ─── 下载详情 ───

    /// <summary>总字节数</summary>
    [ObservableProperty] private long _totalBytes;

    /// <summary>已下载字节数</summary>
    [ObservableProperty] private long _receivedBytes;

    /// <summary>下载速度（字节/秒）</summary>
    [ObservableProperty] private double _speedBytesPerSec;

    /// <summary>预估剩余时间</summary>
    [ObservableProperty] private TimeSpan? _eta;

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

    /// <summary>是否显示取消按钮（下载中显示，点击取消下载）</summary>
    [ObservableProperty] private bool _isCancelVisible;

    // ─── UI 计算属性 ───

    /// <summary>是否显示下载进度区域</summary>
    public bool ShowProgressBar =>
        Stage is UpdateStage.Downloading;

    /// <summary>是否显示下载详情（网速/大小/时间）</summary>
    public bool ShowDownloadDetail => Stage == UpdateStage.Downloading;

    /// <summary>左边按钮文本（取消下载 / 稍后）</summary>
    public string LeftButtonText => Stage == UpdateStage.Downloading ? "取消下载" : "稍后";

    /// <summary>按钮文本</summary>
    public string ActionButtonText => Stage switch
    {
        UpdateStage.UpdateAvailable => "下载更新",
        UpdateStage.Downloading     => "后台下载",
        UpdateStage.ReadyToInstall => "安装更新",
        UpdateStage.Failed          => "重试",
        _                           => "检查更新"
    };

    /// <summary>进度整数（0-100，便于绑定）</summary>
    public int ProgressInt => (int)Math.Round(Progress);

    /// <summary>网速显示文本</summary>
    public string SpeedDisplay =>
        SpeedBytesPerSec >= 1_048_576
            ? $"{SpeedBytesPerSec / 1_048_576:F1} MB/s"
            : SpeedBytesPerSec > 0
                ? $"{SpeedBytesPerSec / 1024:F0} KB/s"
                : "";

    /// <summary>已下载 / 总大小文本</summary>
    public string SizeDisplay =>
        TotalBytes > 0
            ? $"{FormatSize(ReceivedBytes)} / {FormatSize(TotalBytes)}"
            : "";

    /// <summary>预估剩余时间文本</summary>
    public string EtaDisplay =>
        Eta.HasValue && Eta.Value.TotalSeconds > 0
            ? $"剩余 {FormatTime((int)Eta.Value.TotalSeconds)}"
            : "";

    /// <summary>进度百分比文本</summary>
    public string ProgressDisplay => $"{(int)Progress}%";

    /// <summary>文件大小显示文本</summary>
    public string FileSizeDisplay => FormatSize(FileSize);

    // ─── 属性变更联动 ───

    partial void OnStageChanged(UpdateStage value)
    {
        OnPropertyChanged(nameof(ShowProgressBar));
        OnPropertyChanged(nameof(ShowDownloadDetail));
        OnPropertyChanged(nameof(ActionButtonText));
        OnPropertyChanged(nameof(LeftButtonText));
        OnPropertyChanged(nameof(ProgressInt));
    }

    partial void OnSpeedBytesPerSecChanged(double value)
        => OnPropertyChanged(nameof(SpeedDisplay));

    partial void OnTotalBytesChanged(long value)
        => OnPropertyChanged(nameof(SizeDisplay));

    partial void OnReceivedBytesChanged(long value)
        => OnPropertyChanged(nameof(SizeDisplay));

    partial void OnEtaChanged(TimeSpan? value)
        => OnPropertyChanged(nameof(EtaDisplay));

    partial void OnProgressChanged(double value)
    {
        OnPropertyChanged(nameof(ProgressDisplay));
        OnPropertyChanged(nameof(ProgressInt));
    }

    partial void OnFileSizeChanged(long value)
        => OnPropertyChanged(nameof(FileSizeDisplay));

    partial void OnIsCancelVisibleChanged(bool value)
        => OnPropertyChanged(nameof(IsCancelVisible));

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

    /// <summary>格式化秒数为人类可读时间</summary>
    private static string FormatTime(int totalSeconds)
    {
        if (totalSeconds <= 0) return "";
        if (totalSeconds < 60) return $"{totalSeconds} 秒";
        return $"{totalSeconds / 60} 分 {totalSeconds % 60} 秒";
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
