using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Quotix.Models;

/// <summary>
/// 更新流水线阶段枚举，驱动 UI 状态切换。
/// </summary>
public enum UpdateStage
{
    /// <summary>初始空闲 — 显示「检查更新」</summary>
    Idle,

    /// <summary>正在检查更新</summary>
    Checking,

    /// <summary>发现新版本 — 显示「下载」</summary>
    UpdateAvailable,

    /// <summary>已是最新版本</summary>
    UpToDate,

    /// <summary>正在下载</summary>
    Downloading,

    /// <summary>正在校验安装包</summary>
    Verifying,

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
    [ObservableProperty] private string _message = "点击检查更新";

    /// <summary>错误信息</summary>
    [ObservableProperty] private string _error = "";

    /// <summary>检测到的新版本号</summary>
    [ObservableProperty] private string _newVersion = "";

    /// <summary>更新包文件大小（字节）</summary>
    [ObservableProperty] private long _fileSize;

    /// <summary>发布日期</summary>
    [ObservableProperty] private string _releaseDate = "";

    /// <summary>更新日志条目</summary>
    [ObservableProperty] private string[] _changelog = System.Array.Empty<string>();

    // ─── UI 计算属性 ───

    /// <summary>是否显示更新详情（版本号、更新日志等）</summary>
    public bool ShowUpdateDetails => Stage == UpdateStage.UpdateAvailable;

    /// <summary>是否显示操作按钮（非进行中状态）</summary>
    public bool ShowActionButton =>
        Stage is UpdateStage.Idle or UpdateStage.UpdateAvailable
            or UpdateStage.ReadyToInstall or UpdateStage.Failed or UpdateStage.UpToDate;

    /// <summary>是否显示 Primary 按钮（检查/下载/重试）</summary>
    public bool ShowPrimaryButton =>
        Stage is UpdateStage.Idle or UpdateStage.UpdateAvailable
            or UpdateStage.Failed or UpdateStage.UpToDate;

    /// <summary>是否显示安装按钮（Success 绿色）</summary>
    public bool ShowInstallButton => Stage == UpdateStage.ReadyToInstall;

    /// <summary>是否显示进度区域</summary>
    public bool ShowProgressBar =>
        Stage is UpdateStage.Downloading or UpdateStage.Verifying;

    /// <summary>是否显示下载详情（网速/大小/时间）</summary>
    public bool ShowDownloadDetail => Stage == UpdateStage.Downloading;

    /// <summary>按钮文本</summary>
    public string ActionButtonText => Stage switch
    {
        UpdateStage.Idle => "检查更新",
        UpdateStage.UpdateAvailable => "下载",
        UpdateStage.ReadyToInstall => "安装",
        UpdateStage.Failed => "重试",
        UpdateStage.UpToDate => "检查更新",
        _ => ""
    };

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
        OnPropertyChanged(nameof(ShowActionButton));
        OnPropertyChanged(nameof(ShowPrimaryButton));
        OnPropertyChanged(nameof(ShowInstallButton));
        OnPropertyChanged(nameof(ShowProgressBar));
        OnPropertyChanged(nameof(ShowDownloadDetail));
        OnPropertyChanged(nameof(ShowUpdateDetails));
        OnPropertyChanged(nameof(ActionButtonText));
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
        => OnPropertyChanged(nameof(ProgressDisplay));

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

    /// <summary>格式化秒数为人类可读时间</summary>
    private static string FormatTime(int totalSeconds)
    {
        if (totalSeconds <= 0) return "";
        if (totalSeconds < 60) return $"{totalSeconds} 秒";
        return $"{totalSeconds / 60} 分 {totalSeconds % 60} 秒";
    }
}
