using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Quotix.Views;

public partial class SplashWindow : Window
{
    // ── 外部信号 ──
    private readonly TaskCompletionSource _externalReady = new();
    private readonly TaskCompletionSource _animationReady = new();
    private volatile bool _fastForwarded;

    /// <summary>外部（如数据库初始化）完成时调用</summary>
    public void SignalExternalReady() => _externalReady.TrySetResult();

    /// <summary>等待动画和外部加载都完成</summary>
    public Task WaitForReadyAsync() => Task.WhenAll(_animationReady.Task, _externalReady.Task);

    // ── 日志项缓存 ──
    private readonly List<(TextBlock check, TextBlock text, double delayMs, double progressPct)> _logItems = [];
    private int _currentLogIndex = -1;

    public SplashWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // ═══════════════════════════════════════════
    //  主时间线
    // ═══════════════════════════════════════════
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadIcon();
        BuildLogItems(); // 预建日志项

        // 设置圆角裁剪区域（基于窗口实际尺寸）
        ContentClip.Rect = new Rect(0, 0, ActualWidth, ActualHeight);

        // ── 0.00s：窗口淡入 ──
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        // ── 0.00s–0.20s：Logo 淡入 + 微缩放 ──
        LogoImage.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.97, 1.0, TimeSpan.FromMilliseconds(200))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.97, 1.0, TimeSpan.FromMilliseconds(200))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        // 启动进度条（独立异步任务）
        _ = AnimateProgressBar();

        await Task.Delay(200);

        // ── 0.20s–0.50s：QUOTIX 逐字母（每 60ms 一个）──
        AnimateLetter(CharQ);
        if (_fastForwarded) goto fastForward;
        await Task.Delay(60);
        AnimateLetter(CharU);
        if (_fastForwarded) goto fastForward;
        await Task.Delay(60);
        AnimateLetter(CharO);
        if (_fastForwarded) goto fastForward;
        await Task.Delay(60);
        AnimateLetter(CharT);
        if (_fastForwarded) goto fastForward;
        await Task.Delay(60);
        AnimateLetter(CharI);
        if (_fastForwarded) goto fastForward;
        await Task.Delay(60);
        AnimateLetter(CharX);

        // ── 0.45s+：加载日志逐步显示 ──
        foreach (var (check, text, delayMs, _) in _logItems)
        {
            if (_fastForwarded) goto fastForward;

            if (_currentLogIndex >= 0)
            {
                // 上一行 → ✓
                var prev = _logItems[_currentLogIndex];
                prev.check.Text = "\u2713"; // ✓
                prev.check.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                prev.text.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
            }

            _currentLogIndex++;
            text.Text = _logItems[_currentLogIndex].text.Text;
            text.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            text.Visibility = Visibility.Visible;
            check.Visibility = Visibility.Visible;
            check.Text = "  ";
            check.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));

            await Task.Delay((int)delayMs);
        }

        // 最后一行 → ✓
        if (_currentLogIndex >= 0)
        {
            var prev = _logItems[_currentLogIndex];
            prev.check.Text = "\u2713";
            prev.check.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
            prev.text.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
        }

        // ── Ready. ──
        await ShowReady();
        goto done;

    fastForward:
        await ShowReady();

    done:
        // 动画序列完成，等待外部信号
        _animationReady.TrySetResult();

        // 如果外部已经 Ready，这个 await 立即返回
        await _externalReady.Task;
    }

    // ═══════════════════════════════════════════
    //  Ready 状态 + Logo 光效
    // ═══════════════════════════════════════════
    private async Task ShowReady()
    {
        // 强行补满进度条
        ProgressFill.BeginAnimation(WidthProperty, null);
        ProgressFill.Width = ActualWidth;

        // 添加 Ready. 行
        var readyCheck = new TextBlock
        {
            Text = "  ", FontSize = 11, FontFamily = new FontFamily("Consolas, Segoe UI, sans-serif"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0)
        };
        var readyText = new TextBlock
        {
            Text = "Ready.", FontSize = 11, FontFamily = new FontFamily("Consolas, Segoe UI, sans-serif"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
            Margin = new Thickness(0, 2, 0, 0)
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(readyCheck);
        row.Children.Add(readyText);
        LogPanel.Children.Add(row);

        // Logo 光效脉冲：100% → 120% → 100% 持续 120ms
        var glowAnim = new DoubleAnimation(0, 0.18, TimeSpan.FromMilliseconds(80))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            AutoReverse = true
        };
        LogoGlow.BeginAnimation(OpacityProperty, glowAnim);

        await Task.Delay(120);
    }

    // ═══════════════════════════════════════════
    //  快速结束（外部加载先完成）
    // ═══════════════════════════════════════════
    public void FastForward()
    {
        if (_fastForwarded) return;
        _fastForwarded = true;
    }

    // ═══════════════════════════════════════════
    //  进度条动画
    // ═══════════════════════════════════════════
    private async Task AnimateProgressBar()
    {
        // 等窗口渲染完拿到 ActualWidth
        await Task.Delay(50);
        var targetWidth = ActualWidth;
        var anim = new DoubleAnimation(0, targetWidth, TimeSpan.FromMilliseconds(1200))
        {
            EasingFunction = new PowerEase { EasingMode = EasingMode.EaseInOut, Power = 1.5 }
        };
        ProgressFill.BeginAnimation(WidthProperty, anim);
    }

    // ═══════════════════════════════════════════
    //  预建日志项
    // ═══════════════════════════════════════════
    private void BuildLogItems()
    {
        AddLogItem("Initializing...",           120);
        AddLogItem("Loading Configuration...",  120);
        AddLogItem("Connecting Database...",    150);
        AddLogItem("Loading Product Library...",120);
        AddLogItem("Loading Components...",     120);
        AddLogItem("Checking License...",        80);
    }

    private void AddLogItem(string label, double delayMs)
    {
        var check = new TextBlock
        {
            Text = "  ", FontSize = 11,
            FontFamily = new FontFamily("Consolas, Segoe UI, sans-serif"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            Visibility = Visibility.Collapsed
        };
        var text = new TextBlock
        {
            Text = label, FontSize = 11,
            FontFamily = new FontFamily("Consolas, Segoe UI, sans-serif"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            Margin = new Thickness(0, 2, 0, 0),
            Visibility = Visibility.Collapsed
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(check);
        row.Children.Add(text);
        LogPanel.Children.Add(row);

        _logItems.Add((check, text, delayMs, 0));
    }

    // ═══════════════════════════════════════════
    //  工具方法
    // ═══════════════════════════════════════════
    private static void AnimateLetter(FrameworkElement element)
    {
        element.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(55))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void LoadIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
            if (File.Exists(iconPath))
            {
                using var stream = new FileStream(iconPath, FileMode.Open, FileAccess.Read);
                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var best = decoder.Frames[0];
                foreach (var f in decoder.Frames)
                    if (f.PixelWidth > best.PixelWidth) best = f;
                LogoImage.Source = best;
            }
        }
        catch { }
    }

    // ═══════════════════════════════════════════
    //  淡出
    // ═══════════════════════════════════════════
    public async Task FadeOutAsync()
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        // 同步提高主窗口透明度，避免黑屏
        var tcs = new TaskCompletionSource<bool>();
        fadeOut.Completed += (_, _) => tcs.SetResult(true);
        BeginAnimation(OpacityProperty, fadeOut);
        await tcs.Task;
        Close();
    }
}
