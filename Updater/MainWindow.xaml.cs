using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Windows;

namespace Quotix.Updater;

public partial class MainWindow : Window
{
    private readonly UpdateRequest _request;
    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _operationCts;
    private bool _isUpdateRunning;
    private bool _allowClose;

    public MainWindow(UpdateRequest request)
    {
        _request = request;
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Quotix-Updater");

        InitializeComponent();
        VersionText.Text = $"v{_request.Version}";
        Loaded += async (_, _) => await RunUpdateAsync();
        Closed += (_, _) =>
        {
            _operationCts?.Cancel();
            _operationCts?.Dispose();
            _httpClient.Dispose();
        };
    }

    private async Task RunUpdateAsync()
    {
        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();

        SetWorkingState();

        try
        {
            ValidateRequest();
            var installerPath = await DownloadAndVerifyAsync(_operationCts.Token);

            StatusText.Text = "正在等待 Quotix 关闭...";
            ClearDownloadMetrics();
            await WaitForMainProcessAsync(_operationCts.Token);

            StatusText.Text = "正在安装新版本...";
            ClearDownloadMetrics();
            DownloadProgress.IsIndeterminate = true;

            await InstallAsync(installerPath, _operationCts.Token);
            TryDelete(installerPath);
            TryDelete(installerPath + ".download");

            StatusText.Text = "更新完成，正在启动 Quotix...";
            StartMainApplication();
            _allowClose = true;
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"更新失败：{ex.Message}";
            ShowRecoveryActions();
        }
    }

    private void ValidateRequest()
    {
        if (string.IsNullOrWhiteSpace(_request.Version)
            || !Uri.TryCreate(_request.DownloadUrl, UriKind.Absolute, out _)
            || _request.Sha256.Length != 64
            || _request.Sha256.Any(value => !Uri.IsHexDigit(value))
            || string.IsNullOrWhiteSpace(_request.MainExecutablePath)
            || string.IsNullOrWhiteSpace(_request.InstallDirectory))
        {
            throw new InvalidDataException("更新元数据不完整");
        }
    }

    private async Task<string> DownloadAndVerifyAsync(CancellationToken cancellationToken)
    {
        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quotix",
            "Updates");
        Directory.CreateDirectory(updateDirectory);

        var installerPath = Path.Combine(updateDirectory, $"Quotix_Setup_{_request.Version}.exe");
        var partialPath = installerPath + ".download";
        var routes = await OrderRoutesAsync(cancellationToken);
        Exception? lastError = null;

        foreach (var route in routes)
        {
            try
            {
                await DownloadRouteAsync(route, partialPath, cancellationToken);
                StatusText.Text = "正在验证更新文件...";
                ClearDownloadMetrics();
                DownloadProgress.IsIndeterminate = true;

                var actualHash = await ComputeSha256Async(partialPath, cancellationToken);
                if (!actualHash.Equals(_request.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(partialPath);
                    throw new InvalidDataException("安装包校验失败");
                }

                if (File.Exists(installerPath))
                    File.Delete(installerPath);
                File.Move(partialPath, installerPath);
                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = 100;
                return installerPath;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new HttpRequestException(lastError?.Message ?? "无法下载安装包", lastError);
    }

    private async Task DownloadRouteAsync(
        string url,
        string partialPath,
        CancellationToken cancellationToken)
    {
        var existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(existingLength, null);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var append = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append)
            existingLength = 0;

        var contentLength = response.Content.Headers.ContentLength ?? 0;
        var totalLength = response.Content.Headers.ContentRange?.Length
            ?? (_request.FileSize > 0
                ? _request.FileSize
                : contentLength > 0
                    ? existingLength + contentLength
                    : 0);
        using var source = await response.Content.ReadAsStreamAsync();
        using var target = new FileStream(
            partialPath,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            128 * 1024,
            useAsync: true);

        StatusText.Text = "正在下载更新文件...";
        DownloadProgress.IsIndeterminate = false;
        DownloadProgress.Value = totalLength > 0
            ? Clamp(existingLength * 100d / totalLength, 0, 100)
            : 0;

        var buffer = new byte[128 * 1024];
        var received = existingLength;
        var sampleBytes = 0L;
        var sampleWatch = Stopwatch.StartNew();
        var smoothedSpeed = 0d;

        while (true)
        {
            var read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (read == 0)
                break;

            await target.WriteAsync(buffer, 0, read, cancellationToken);
            received += read;
            sampleBytes += read;

            if (sampleWatch.ElapsedMilliseconds < 500)
                continue;

            var instantSpeed = sampleBytes / sampleWatch.Elapsed.TotalSeconds;
            smoothedSpeed = smoothedSpeed <= 0
                ? instantSpeed
                : smoothedSpeed * 0.72 + instantSpeed * 0.28;
            sampleBytes = 0;
            sampleWatch.Restart();

            UpdateProgress(received, totalLength, smoothedSpeed);
        }

        UpdateProgress(received, totalLength, smoothedSpeed);
    }

    private async Task<IReadOnlyList<string>> OrderRoutesAsync(CancellationToken cancellationToken)
    {
        var direct = _request.DownloadUrl;
        var mirror = $"https://ghfast.top/{direct}";
        var candidates = new[] { direct, mirror };

        var probes = candidates.Select(async url =>
        {
            var speed = await ProbeAsync(url, cancellationToken);
            return (url, speed);
        });

        return (await Task.WhenAll(probes))
            .OrderByDescending(result => result.speed)
            .Select(result => result.url)
            .ToArray();
    }

    private async Task<double> ProbeAsync(string url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, 262143);
            var watch = Stopwatch.StartNew();
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[64 * 1024];
            var total = 0;
            while (total < 262144)
            {
                var read = await stream.ReadAsync(
                    buffer,
                    0,
                    Math.Min(buffer.Length, 262144 - total),
                    timeout.Token);
                if (read == 0)
                    break;
                total += read;
            }
            return total / Math.Max(watch.Elapsed.TotalSeconds, 0.01);
        }
        catch
        {
            return 0;
        }
    }

    private void UpdateProgress(long received, long total, double speed)
    {
        Dispatcher.Invoke(() =>
        {
            DownloadProgress.IsIndeterminate = false;
            if (total > 0)
                DownloadProgress.Value = Clamp(received * 100d / total, 0, 100);

            SizeText.Text = total > 0
                ? $"{FormatSize(received)} / {FormatSize(total)}"
                : FormatSize(received);

            if (speed <= 0)
            {
                SpeedText.Text = "";
                return;
            }

            var eta = total > received
                ? TimeSpan.FromSeconds((total - received) / speed)
                : TimeSpan.Zero;
            SpeedText.Text = $"{FormatSize((long)speed)}/s  剩余 {FormatEta(eta)}";
        });
    }

    private async Task WaitForMainProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(_request.MainProcessId);
            while (!process.HasExited)
            {
                await Task.Delay(200, cancellationToken);
                process.Refresh();
            }
        }
        catch (ArgumentException)
        {
        }
    }

    private async Task InstallAsync(string installerPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = string.Join(" ", new[]
            {
                "/VERYSILENT",
                "/SUPPRESSMSGBOXES",
                "/NORESTART",
                "/CLOSEAPPLICATIONS",
                QuoteArgument("/DIR=" + _request.InstallDirectory)
            })
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动安装程序");
        while (!process.HasExited)
            await Task.Delay(200, cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"安装程序返回错误代码 {process.ExitCode}");
    }

    private void StartMainApplication()
    {
        if (!File.Exists(_request.MainExecutablePath))
            throw new FileNotFoundException("更新完成，但未找到 Quotix", _request.MainExecutablePath);

        Process.Start(new ProcessStartInfo
        {
            FileName = _request.MainExecutablePath,
            UseShellExecute = true
        });
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
        => await RunUpdateAsync();

    private void ReturnButton_Click(object sender, RoutedEventArgs e)
    {
        StartMainApplication();
        _allowClose = true;
        Application.Current.Shutdown();
    }

    private void SetWorkingState()
    {
        _isUpdateRunning = true;
        RetryButton.Visibility = Visibility.Collapsed;
        ReturnButton.Visibility = Visibility.Collapsed;
        DownloadProgress.IsIndeterminate = true;
        DownloadProgress.Value = 0;
        SizeText.Text = "";
        SpeedText.Text = "";
        StatusText.Text = "等待中...";
    }

    private void ShowRecoveryActions()
    {
        _isUpdateRunning = false;
        DownloadProgress.IsIndeterminate = false;
        ClearDownloadMetrics();
        RetryButton.Visibility = Visibility.Visible;
        ReturnButton.Visibility = Visibility.Visible;
    }

    private void ClearDownloadMetrics()
    {
        SizeText.Text = "";
        SpeedText.Text = "";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isUpdateRunning && !_allowClose)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(stream);
            cancellationToken.ThrowIfCancellationRequested();
            return BitConverter.ToString(hash).Replace("-", "");
        }, cancellationToken);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_048_576)
            return $"{bytes / 1_048_576d:F1} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024d:F1} KB";
        return $"{bytes.ToString(CultureInfo.InvariantCulture)} B";
    }

    private static string FormatEta(TimeSpan eta)
    {
        if (eta.TotalSeconds < 60)
            return $"{Math.Max(1, (int)Math.Ceiling(eta.TotalSeconds))} 秒";
        return $"{(int)eta.TotalMinutes} 分 {eta.Seconds} 秒";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static double Clamp(double value, double minimum, double maximum)
        => Math.Max(minimum, Math.Min(maximum, value));

    private static string QuoteArgument(string value)
        => "\"" + value.Replace("\"", "\\\"") + "\"";
}
