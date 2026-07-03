using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace QuotixCloudBuild;

public partial class MainWindow : Window
{
    private readonly string _projectDir;
    private CancellationTokenSource? _cts;
    private bool _isBuilding;

    public MainWindow()
    {
        InitializeComponent();
        _projectDir = Directory.GetCurrentDirectory();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var csprojPath = Path.Combine(_projectDir, "QuotixDesktop.csproj");
        if (File.Exists(csprojPath))
        {
            var content = File.ReadAllText(csprojPath);
            var match = System.Text.RegularExpressions.Regex.Match(content, @"<Version>([^<]+)</Version>");
            if (match.Success)
            {
                CurrentVersionBox.Text = match.Groups[1].Value;
                NewVersionBox.Text = match.Groups[1].Value;
            }
        }
        else
        {
            AppendLog("警告：未找到 QuotixDesktop.csproj");
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBuilding) return;

        var newVersion = NewVersionBox.Text.Trim();
        var changelog = ChangelogBox.Text.Trim();

        if (string.IsNullOrEmpty(newVersion))
        {
            MessageBox.Show("请输入版本号", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(newVersion, @"^\d+\.\d+\.\d+$"))
        {
            MessageBox.Show("版本号格式错误，应为 x.y.z", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _isBuilding = true;
        _cts = new CancellationTokenSource();
        StartButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ProgressBorder.Visibility = Visibility.Visible;
        ProgressText.Text = "正在准备...";
        BuildStatusText.Text = "";
        LogBox.Text = "";

        try
        {
            await RunBuildAsync(newVersion, changelog, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendLog("已取消");
            ProgressText.Text = "已取消";
        }
        catch (Exception ex)
        {
            AppendLog($"错误：{ex.Message}");
            ProgressText.Text = "构建失败";
        }
        finally
        {
            _isBuilding = false;
            StartButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private async Task RunBuildAsync(string version, string changelog, CancellationToken ct)
    {
        // Step 1: 更新 csproj
        AppendLog($"更新版本号到 {version}...");
        ProgressText.Text = "更新版本号...";
        await Task.Run(() =>
        {
            var csprojPath = Path.Combine(_projectDir, "QuotixDesktop.csproj");
            var content = File.ReadAllText(csprojPath);
            // 使用简单的字符串替换，避免正则替换字符串中的 $1/$2 歧义
            content = System.Text.RegularExpressions.Regex.Replace(content,
                @"<Version>[^<]+</Version>", $"<Version>{version}</Version>");
            content = System.Text.RegularExpressions.Regex.Replace(content,
                @"<InformationalVersion>[^<]+</InformationalVersion>", $"<InformationalVersion>{version}</InformationalVersion>");
            File.WriteAllText(csprojPath, content, Encoding.UTF8);
        }, ct);

        // Step 2: 生成 latest.yml
        AppendLog("生成 latest.yml...");
        ProgressText.Text = "生成 latest.yml...";
        await Task.Run(() =>
        {
            var outDir = Path.Combine(_projectDir, "Installer", "Out");
            Directory.CreateDirectory(outDir);
            var ymlPath = Path.Combine(outDir, "latest.yml");
            var lines = changelog.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l));
            var ymlContent = $"version: {version}\nchangelog: |\n";
            foreach (var line in lines)
            {
                ymlContent += $"  {line}\n";
            }
            File.WriteAllText(ymlPath, ymlContent, Encoding.UTF8);
        }, ct);

        // Step 3: Git commit & push
        AppendLog("提交代码...");
        ProgressText.Text = "提交代码...";

        // git add
        var addResult = await RunCmdAsync("git", $"add -A", ct);
        AppendLog($"git add: {addResult}");

        // 检查是否有变动（使用正确的 git diff --cached --quiet 退出码判断）
        var (_, exitCode) = await RunGitCommandAsync("diff --cached --quiet", ct);
        var hasChanges = exitCode != 0;

        if (!hasChanges)
        {
            AppendLog("警告：没有文件变动，跳过 commit");
        }
        else
        {
            // commit message 只用版本号和标题（不包含 changelog）
            var commitTitle = $"Release v{version}";
            var msgFile = Path.Combine(Path.GetTempPath(), "quotix_commit_msg.txt");
            await File.WriteAllTextAsync(msgFile, commitTitle, Encoding.UTF8, ct);

            var commitOutput = await RunCmdAsync("git", $"commit -F \"{msgFile}\"", ct);
            AppendLog($"git commit: {commitOutput}");
        }

        var pushOutput = await RunCmdAsync("git", "push origin main", ct);
        AppendLog($"git push: {pushOutput}");

        // Step 4: 创建并推送 tag
        AppendLog($"创建 tag v{version}...");
        ProgressText.Text = "创建 tag...";

        await RunCmdAsync("git", $"tag -d v{version}", ct);
        await RunCmdAsync("git", $"push origin --delete v{version}", ct);

        await RunCmdAsync("git", $"tag v{version}", ct);
        var tagPushOutput = await RunCmdAsync("git", $"push origin v{version}", ct);
        AppendLog($"tag push: {tagPushOutput}");

        // Step 5: 监控 GitHub Actions
        AppendLog("监控构建进度...");
        ProgressText.Text = "等待构建开始...";
        BuildStatusText.Text = "⏳ 队列中...";

        var repo = "Grafdustin/Quotix";
        var maxWait = TimeSpan.FromMinutes(10);
        var startTime = DateTime.Now;

        while (DateTime.Now - startTime < maxWait)
        {
            ct.ThrowIfCancellationRequested();

            var runJson = await RunCmdAsync("gh", $"run list --repo {repo} --workflow build-and-release.yml --limit 1 --json status,conclusion", ct);
            if (!string.IsNullOrWhiteSpace(runJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(runJson);
                    var root = doc.RootElement;
                    var status = root.GetProperty("status").GetString();
                    var conclusion = root.TryGetProperty("conclusion", out var c) ? c.GetString() : null;

                    BuildStatusText.Text = status switch
                    {
                        "queued" => "⏳ 队列中...",
                        "in_progress" => "🔄 构建中...",
                        "completed" when conclusion == "success" => "✅ 构建成功！",
                        "completed" => $"❌ 构建失败：{conclusion}",
                        _ => status ?? ""
                    };

                    if (status == "completed")
                    {
                        if (conclusion == "success")
                        {
                            ProgressText.Text = "构建完成！";
                            AppendLog("✅ 构建成功！");
                            AppendLog($"下载：https://github.com/{repo}/releases/tag/v{version}");
                            MessageBox.Show(
                                $"构建成功！\n\n下载地址：\nhttps://github.com/{repo}/releases/tag/v{version}",
                                "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            ProgressText.Text = "构建失败";
                            AppendLog($"❌ 构建失败：{conclusion}");
                        }
                        return;
                    }
                }
                catch { }
            }

            await Task.Delay(15000, ct);
        }

        AppendLog("超时，请手动查看：https://github.com/Grafdustin/Quotix/actions");
    }

    private async Task<string> RunCmdAsync(string fileName, string args, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                WorkingDirectory = _projectDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var proc = Process.Start(psi);
            if (proc == null) return "无法启动进程";
            // 同步读取（在 Task.Run 中）
            var output = proc.StandardOutput.ReadToEnd();
            var error = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return output + error;
        }, ct);
    }

    private async Task<(string output, int exitCode)> RunGitCommandAsync(string args, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = _projectDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var proc = Process.Start(psi);
            if (proc == null) return ("无法启动进程", -1);
            var output = proc.StandardOutput.ReadToEnd();
            var error = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return (output + error, proc.ExitCode);
        }, ct);
    }

    private void AppendLog(string text)
    {
        Dispatcher.Invoke(() =>
        {
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n");
            LogBox.ScrollToEnd();
        });
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        CancelButton.IsEnabled = false;
    }
}
