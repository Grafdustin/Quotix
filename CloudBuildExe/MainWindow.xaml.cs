using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace QuotixCloudBuild;

/// <summary>
/// Quotix 云端发布主窗口，自动化版本更新、Git 发布和 GitHub Actions 监控。
/// </summary>
public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly string _projectDir;
    private CancellationTokenSource? _cts;
    private bool _isBuilding;

    /// <summary>
    /// GitHub 仓库标识。
    /// </summary>
    private const string Repo = "Grafdustin/Quotix";

    /// <summary>
    /// GitHub Actions workflow 文件名。
    /// </summary>
    private const string WorkflowFile = "build-and-release.yml";

    /// <summary>
    /// 构建监控最大等待时间。
    /// </summary>
    private static readonly TimeSpan MaxBuildWait = TimeSpan.FromMinutes(10);

    /// <summary>
    /// GitHub Actions run 监控轮询间隔。
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(8);

    /// <summary>
    /// 等待 workflow 启动的最大时间。
    /// </summary>
    private static readonly TimeSpan WorkflowStartWait = TimeSpan.FromMinutes(2);

    private readonly Stopwatch _buildTimer = new();

    public MainWindow()
    {
        InitializeComponent();
        _projectDir = FindProjectDir(AppContext.BaseDirectory);
        Loaded += OnLoaded;
    }

    /// <summary>
    /// 向上查找包含 QuotixDesktop.csproj 的项目根目录。
    /// </summary>
    private static string FindProjectDir(string startDir)
    {
        var dir = startDir;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "QuotixDesktop.csproj")))
                return dir;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return startDir;
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
            AppendLog("⚠ 未找到 QuotixDesktop.csproj，请确保此工具位于项目目录或其子目录中");
            StatusHintText.Text = "⚠ 未检测到项目文件";
            StatusHintText.Visibility = Visibility.Visible;
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBuilding) return;

        var newVersion = NewVersionBox.Text.Trim();
        var changelog = ChangelogBox.Text.Trim();

        if (string.IsNullOrEmpty(newVersion))
        {
            ShowDialog("请输入版本号", "提示");
            return;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(newVersion, @"^\d+\.\d+\.\d+$"))
        {
            ShowDialog("版本号格式错误，应为 x.y.z（例如 1.0.57）", "格式错误");
            return;
        }

        _isBuilding = true;
        _cts = new CancellationTokenSource();
        _buildTimer.Restart();

        StartButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ProgressBorder.Visibility = Visibility.Visible;
        ProgressText.Text = "正在准备...";
        ProgressBar.Value = 0;
        BuildStatusText.Text = "";
        LogBox.Text = "";
        StatusHintText.Visibility = Visibility.Collapsed;

        // 启动耗时计时器
        var elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        elapsedTimer.Tick += (_, _) => UpdateElapsedDisplay();
        elapsedTimer.Start();

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
            _buildTimer.Stop();
            elapsedTimer.Stop();
            _isBuilding = false;
            StartButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            UpdateElapsedDisplay();
        }
    }

    private async Task RunBuildAsync(string version, string changelog, CancellationToken ct)
    {
        var cleanChangelog = CleanChangelog(changelog);

        // Step 1: 更新 csproj → 20%
        AppendLog($"更新版本号到 {version}...");
        ProgressText.Text = "更新版本号...";
        UpdateProgress(20);

        await Task.Run(() =>
        {
            var csprojPath = Path.Combine(_projectDir, "QuotixDesktop.csproj");
            var content = File.ReadAllText(csprojPath);
            content = System.Text.RegularExpressions.Regex.Replace(content,
                @"<Version>[^<]+</Version>", $"<Version>{version}</Version>");
            content = System.Text.RegularExpressions.Regex.Replace(content,
                @"<InformationalVersion>[^<]+</InformationalVersion>", $"<InformationalVersion>{version}</InformationalVersion>");
            File.WriteAllText(csprojPath, content, Encoding.UTF8);
        }, ct);

        // Step 2: 生成 latest.yml → 40%
        AppendLog("生成 latest.yml...");
        ProgressText.Text = "生成 latest.yml...";
        UpdateProgress(40);

        await Task.Run(() =>
        {
            var outDir = Path.Combine(_projectDir, "Installer", "Out");
            Directory.CreateDirectory(outDir);
            var ymlPath = Path.Combine(outDir, "latest.yml");

            var lines = cleanChangelog.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("<!--") && !l.Contains("AssemblyVersion"));
            var ymlContent = $"version: {version}\nchangelog: |\n";
            foreach (var line in lines)
                ymlContent += $"  {line}\n";
            File.WriteAllText(ymlPath, ymlContent, Encoding.UTF8);
            AppendLog($"latest.yml 已生成，包含 {lines.Count()} 条记录");
        }, ct);

        // Step 3: Git commit & push → 60%
        AppendLog("提交代码...");
        ProgressText.Text = "提交代码...";
        UpdateProgress(60);

        await RunCmdAsync("git", "add -A", ct);

        // 使用 git diff --cached --quiet 判断是否有变更（退出码 1 = 有变更，0 = 无变更）
        var (_, exitCode) = await RunGitCommandAsync("diff --cached --quiet", ct);
        if (exitCode != 0)
        {
            var commitTitle = $"Release v{version}";
            var msgFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(msgFile, commitTitle, Encoding.UTF8, ct);
                // 使用 @ 前缀防止路径含空格的问题
                await RunCmdAsync("git", $"commit -F @\"{msgFile}\"", ct);
            }
            finally
            {
                try { File.Delete(msgFile); } catch { /* 忽略清理失败 */ }
            }
        }
        else
        {
            AppendLog("没有文件变动，跳过 commit");
        }

        await RunCmdAsync("git", "push origin main", ct);

        // Step 4: 创建并推送 tag → 80%
        AppendLog($"创建 tag v{version}...");
        ProgressText.Text = "创建 tag...";
        UpdateProgress(80);

        // 删除旧 tag（忽略失败）
        try { await RunCmdAsync("git", $"tag -d v{version}", ct); } catch { }
        try { await RunCmdAsync("git", $"push origin --delete v{version}", ct); } catch { }

        await RunCmdAsync("git", $"tag v{version}", ct);
        await RunCmdAsync("git", $"push origin v{version}", ct);
        AppendLog($"✅ tag v{version} 已推送");

        // Step 5: 监控 GitHub Actions → 90-100%
        ProgressText.Text = "等待构建开始...";
        UpdateProgress(90);
        BuildStatusText.Text = "等待 GitHub Actions 启动...";

        var runId = await WaitForWorkflowRunAsync(ct);

        if (runId == null)
        {
            AppendLog($"无法找到触发的 workflow run，请手动查看：https://github.com/{Repo}/actions");
            ProgressText.Text = "监控失败";
            return;
        }

        AppendLog($"找到 workflow run: #{runId}");
        AppendLog($"查看详情：https://github.com/{Repo}/actions/runs/{runId}");

        await MonitorWorkflowRunAsync(runId, version, ct);
    }

    /// <summary>
    /// 等待并识别刚触发的 GitHub Actions workflow run。
    /// 通过记录推送前的最新 run ID，与推送后对比来确定新 run。
    /// </summary>
    private async Task<string?> WaitForWorkflowRunAsync(CancellationToken ct)
    {
        AppendLog("等待 GitHub Actions workflow 启动...");
        var deadline = DateTime.Now.Add(WorkflowStartWait);

        while (DateTime.Now < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var runListJson = await RunCmdAsync("gh",
                $"run list --repo {Repo} --workflow {WorkflowFile} --limit 3 --json databaseId,status,createdAt", ct);

            if (!string.IsNullOrWhiteSpace(runListJson) && runListJson.TrimStart().StartsWith("["))
            {
                try
                {
                    using var doc = JsonDocument.Parse(runListJson);
                    var array = doc.RootElement;

                    if (array.GetArrayLength() > 0)
                    {
                        // 检查最新的 run 是否是最近 2 分钟内创建的
                        var latestRun = array[0];
                        var createdAt = latestRun.GetProperty("createdAt").GetString();
                        if (createdAt != null)
                        {
                            var createdTime = DateTime.Parse(createdAt);
                            if (createdTime > DateTime.UtcNow.AddMinutes(-2))
                            {
                                var newRunId = latestRun.GetProperty("databaseId").GetInt64().ToString();
                                var status = latestRun.GetProperty("status").GetString();
                                AppendLog($"✅ 检测到新 run: #{newRunId} ({status})");
                                return newRunId;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"解析 workflow 列表失败：{ex.Message}");
                }
            }

            await Task.Delay(PollInterval, ct);
        }

        return null;
    }

    /// <summary>
    /// 监控指定 workflow run 直到完成，实时显示步骤状态。
    /// </summary>
    private async Task MonitorWorkflowRunAsync(string runId, string version, CancellationToken ct)
    {
        var startTime = DateTime.Now;
        var lastStatus = "";
        var lastStepName = "";
        var startTimeUtc = DateTime.UtcNow; // 用于更可靠地判断 run 是否是我们触发的

        while (DateTime.Now - startTime < MaxBuildWait)
        {
            ct.ThrowIfCancellationRequested();

            var runJson = await RunCmdAsync("gh",
                $"run view {runId} --repo {Repo} --json status,conclusion,steps", ct);

            if (!string.IsNullOrWhiteSpace(runJson) && runJson.TrimStart().StartsWith("{"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(runJson);
                    var run = doc.RootElement;
                    var status = run.GetProperty("status").GetString();
                    var conclusion = run.TryGetProperty("conclusion", out var c) ? c.GetString() : null;

                    // 解析当前正在执行的步骤
                    if (run.TryGetProperty("steps", out var steps))
                    {
                        var currentStep = "";
                        var completedCount = 0;
                        var totalSteps = 0;

                        foreach (var step in steps.EnumerateArray())
                        {
                            totalSteps++;
                            var stepStatus = step.GetProperty("status").GetString();
                            var stepName = step.GetProperty("name").GetString();

                            if (stepStatus == "in_progress")
                                currentStep = stepName ?? "";
                            if (stepStatus == "completed")
                                completedCount++;
                        }

                        // 更新进度（90-98%区间映射到步骤进度）
                        if (totalSteps > 0 && totalSteps > completedCount)
                        {
                            var stepProgress = 90 + (double)completedCount / totalSteps * 8;
                            UpdateProgress((int)stepProgress);
                        }

                        if (!string.IsNullOrEmpty(currentStep) && currentStep != lastStepName)
                        {
                            lastStepName = currentStep;
                            ProgressText.Text = $"正在执行：{currentStep}";
                        }
                    }

                    // 状态变化时更新
                    if (status != lastStatus)
                    {
                        lastStatus = status ?? "";
                        AppendLog($"构建状态：{status}");
                    }

                    BuildStatusText.Text = status switch
                    {
                        "queued" => "队列中，等待执行...",
                        "in_progress" => "构建进行中...",
                        "completed" when conclusion == "success" => "✅ 构建成功！",
                        "completed" => $"❌ 构建失败：{conclusion}",
                        _ => status ?? "未知"
                    };

                    if (status == "completed")
                    {
                        UpdateProgress(100);
                        _buildTimer.Stop();

                        if (conclusion == "success")
                        {
                            var releaseUrl = $"https://github.com/{Repo}/releases/tag/v{version}";
                            var actionsUrl = $"https://github.com/{Repo}/actions/runs/{runId}";
                            ProgressText.Text = "构建完成！";

                            AppendLog("✅ 构建成功！");
                            AppendLog($"耗时：{_buildTimer.Elapsed.TotalMinutes:F1} 分钟");
                            AppendLog($"下载：{releaseUrl}");
                            AppendLog($"详情：{actionsUrl}");

                            StatusHintText.Text = $"✅ 发布完成 · 耗时 {_buildTimer.Elapsed.TotalMinutes:F1} 分钟";
                            StatusHintText.Visibility = Visibility.Visible;

                            ShowDialog(
                                $"发布成功！\n\n版本：v{version}\n耗时：{_buildTimer.Elapsed.TotalMinutes:F1} 分钟\n\n下载地址：\n{releaseUrl}",
                                "完成");

                            // 自动打开浏览器
                            try
                            {
                                Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true });
                            }
                            catch { }
                        }
                        else
                        {
                            ProgressText.Text = "构建失败";
                            AppendLog($"❌ 构建失败：{conclusion}");

                            // 获取失败日志
                            AppendLog("正在获取失败日志...");
                            try
                            {
                                var logs = await RunCmdAsync("gh",
                                    $"run view {runId} --repo {Repo} --log-failed", ct);
                                if (!string.IsNullOrWhiteSpace(logs))
                                    AppendLog($"失败日志：\n{logs}");
                            }
                            catch (Exception ex)
                            {
                                AppendLog($"获取失败日志出错：{ex.Message}");
                            }

                            AppendLog($"查看详情：https://github.com/{Repo}/actions/runs/{runId}");
                            StatusHintText.Text = "❌ 构建失败";
                            StatusHintText.Visibility = Visibility.Visible;
                        }
                        return;
                    }
                }
                catch (JsonException ex)
                {
                    AppendLog($"解析 JSON 失败：{ex.Message}");
                }
            }

            await Task.Delay(PollInterval, ct);
        }

        AppendLog($"监控超时（{MaxBuildWait.TotalMinutes} 分钟），请手动查看：https://github.com/{Repo}/actions");
        AppendLog($"Run ID: {runId}");
        StatusHintText.Text = "⚠ 监控超时";
        StatusHintText.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 更新进度条（线程安全）。
    /// </summary>
    private void UpdateProgress(int value)
    {
        Dispatcher.Invoke(() => ProgressBar.Value = value);
    }

    /// <summary>
    /// 更新耗时显示（线程安全）。
    /// </summary>
    private void UpdateElapsedDisplay()
    {
        Dispatcher.Invoke(() =>
        {
            ElapsedText.Text = _buildTimer.IsRunning
                ? $"已用时 {_buildTimer.Elapsed:mm\\:ss}"
                : $"用时 {_buildTimer.Elapsed:mm\\:ss}";
        });
    }

    /// <summary>
    /// 清理更新日志文本：移除 XML 注释、版本信息等无关内容。
    /// </summary>
    private static string CleanChangelog(string changelog)
    {
        return string.Join('\n',
            changelog.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l =>
                    !string.IsNullOrEmpty(l) &&
                    !l.StartsWith("<!--") &&
                    !l.StartsWith("-->") &&
                    !l.Contains("AssemblyVersion") &&
                    !l.Contains("FileVersion") &&
                    !l.Contains("InformationalVersion"))
        );
    }

    /// <summary>
    /// 执行外部命令并返回合并后的标准输出和标准错误。
    /// 使用异步读取避免死锁。
    /// </summary>
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

            // 异步读取 stdout/stderr 避免缓冲区满导致死锁
            var outputTask = proc.StandardOutput.ReadToEndAsync();
            var errorTask = proc.StandardError.ReadToEndAsync();
            proc.WaitForExit();

            return outputTask.Result + errorTask.Result;
        }, ct);
    }

    /// <summary>
    /// 执行 Git 命令，返回输出内容和退出码。
    /// </summary>
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

            var outputTask = proc.StandardOutput.ReadToEndAsync();
            var errorTask = proc.StandardError.ReadToEndAsync();
            proc.WaitForExit();

            return (outputTask.Result + errorTask.Result, proc.ExitCode);
        }, ct);
    }

    /// <summary>
    /// 向日志区追加一行，带时间戳（线程安全）。
    /// </summary>
    private void AppendLog(string text)
    {
        Dispatcher.Invoke(() =>
        {
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n");
            LogBox.ScrollToEnd();
        });
    }

    /// <summary>
    /// 显示信息弹窗。
    /// </summary>
    private static void ShowDialog(string message, string title)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        CancelButton.IsEnabled = false;
    }
}
