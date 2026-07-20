using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace Quotix.Services;

/// <summary>
/// 问题反馈邮件发送服务。
/// </summary>
public sealed class FeedbackService
{
    private const string SmtpHost = "smtp.163.com";
    private const int SmtpPort = 465;
    private const string SenderAddress = "workpkf1@163.com";
    private const string SenderPassword = "CJkwZ84KZyd9Qk4M";
    private const string RecipientAddress = "kd4a22j@outlook.com";

    /// <summary>发送问题反馈邮件。</summary>
    public async Task SendAsync(FeedbackRequest request)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(SmtpHost, SmtpPort);

        await using var ssl = new SslStream(tcp.GetStream(), false);
        await ssl.AuthenticateAsClientAsync(SmtpHost);

        using var reader = new StreamReader(ssl, Encoding.ASCII, leaveOpen: true);
        await using var writer = new StreamWriter(ssl, Encoding.ASCII, leaveOpen: true)
        {
            NewLine = "\r\n",
            AutoFlush = true
        };

        await ReadExpectedAsync(reader, 220);
        await SendCommandAsync(writer, reader, $"EHLO {Environment.MachineName}", 250);
        await SendCommandAsync(writer, reader, "AUTH LOGIN", 334);
        await SendCommandAsync(writer, reader, Convert.ToBase64String(Encoding.UTF8.GetBytes(SenderAddress)), 334);
        await SendCommandAsync(writer, reader, Convert.ToBase64String(Encoding.UTF8.GetBytes(SenderPassword)), 235);
        await SendCommandAsync(writer, reader, $"MAIL FROM:<{SenderAddress}>", 250);
        await SendCommandAsync(writer, reader, $"RCPT TO:<{RecipientAddress}>", 250);
        await SendCommandAsync(writer, reader, "DATA", 354);

        await writer.WriteAsync(BuildMimeMessage(request));
        await writer.WriteLineAsync(".");
        await ReadExpectedAsync(reader, 250);
        await SendCommandAsync(writer, reader, "QUIT", 221);
    }

    /// <summary>查找当前程序可附加的错误日志。</summary>
    public string? FindErrorLogPath()
    {
        var candidates = new[]
        {
            AppPaths.ErrorLogPath,
            Path.Combine(AppPaths.DataDir, "logs", "error.log"),
            Path.Combine(AppPaths.DataDir, "update-install.log"),
            Path.Combine(AppContext.BaseDirectory, "error.log")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string BuildBody(FeedbackRequest request)
    {
        var attachments = request.AttachmentNames.Count == 0
            ? "无"
            : string.Join(Environment.NewLine, request.AttachmentNames);

        return $"""
版本:
v{AppInfo.Version}

系统:
{RuntimeInformation.OSDescription}

问题类型:
{request.ProblemType}

问题描述:
{request.Description}

附件:
{attachments}
""";
    }

    private static string BuildMimeMessage(FeedbackRequest request)
    {
        var boundary = "----QuotixFeedback" + Guid.NewGuid().ToString("N");
        var subject = EncodeHeader($"[Quotix Bug&反馈] {request.ProblemType}");
        var builder = new StringBuilder();

        builder.AppendLine($"From: Quotix Feedback <{SenderAddress}>");
        builder.AppendLine($"To: {RecipientAddress}");
        builder.AppendLine($"Subject: {subject}");
        builder.AppendLine("MIME-Version: 1.0");
        builder.AppendLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
        builder.AppendLine();

        builder.AppendLine($"--{boundary}");
        builder.AppendLine("Content-Type: text/plain; charset=utf-8");
        builder.AppendLine("Content-Transfer-Encoding: base64");
        builder.AppendLine();
        AppendBase64(builder, Encoding.UTF8.GetBytes(BuildBody(request)));

        foreach (var path in request.AttachmentPaths)
        {
            builder.AppendLine($"--{boundary}");
            builder.AppendLine($"Content-Type: application/octet-stream; name=\"{EncodeHeader(Path.GetFileName(path))}\"");
            builder.AppendLine("Content-Transfer-Encoding: base64");
            builder.AppendLine($"Content-Disposition: attachment; filename=\"{EncodeHeader(Path.GetFileName(path))}\"");
            builder.AppendLine();
            AppendBase64(builder, File.ReadAllBytes(path));
        }

        builder.AppendLine($"--{boundary}--");
        return builder.ToString();
    }

    private static string EncodeHeader(string value)
        => $"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";

    private static void AppendBase64(StringBuilder builder, byte[] bytes)
    {
        var base64 = Convert.ToBase64String(bytes);
        for (var i = 0; i < base64.Length; i += 76)
            builder.AppendLine(base64.Substring(i, Math.Min(76, base64.Length - i)));
        builder.AppendLine();
    }

    private static async Task SendCommandAsync(
        StreamWriter writer,
        StreamReader reader,
        string command,
        int expectedCode)
    {
        await writer.WriteLineAsync(command);
        await ReadExpectedAsync(reader, expectedCode);
    }

    private static async Task ReadExpectedAsync(StreamReader reader, int expectedCode)
    {
        string? line;
        do
        {
            line = await reader.ReadLineAsync();
            if (line == null)
                throw new InvalidOperationException("SMTP 服务器提前断开连接。");
            if (line.Length < 3 || !int.TryParse(line[..3], out var code) || code != expectedCode)
                throw new InvalidOperationException($"SMTP 返回异常：{line}");
        }
        while (line.Length > 3 && line[3] == '-');
    }
}

/// <summary>问题反馈内容。</summary>
public sealed class FeedbackRequest
{
    public string ProblemType { get; init; } = "";
    public string Description { get; init; } = "";
    public IReadOnlyList<string> ScreenshotPaths { get; init; } = Array.Empty<string>();
    public bool AttachErrorLog { get; init; }
    public string? ErrorLogPath { get; init; }

    public IReadOnlyList<string> AttachmentPaths
    {
        get
        {
            var paths = ScreenshotPaths.Where(File.Exists).ToList();
            if (AttachErrorLog && !string.IsNullOrWhiteSpace(ErrorLogPath) && File.Exists(ErrorLogPath))
                paths.Add(ErrorLogPath);
            return paths;
        }
    }

    public IReadOnlyList<string> AttachmentNames
    {
        get
        {
            var names = new List<string>();
            foreach (var path in ScreenshotPaths.Where(File.Exists))
                names.Add(Path.GetFileName(path));
            if (AttachErrorLog && !string.IsNullOrWhiteSpace(ErrorLogPath) && File.Exists(ErrorLogPath))
                names.Add(Path.GetFileName(ErrorLogPath));
            return names;
        }
    }
}
