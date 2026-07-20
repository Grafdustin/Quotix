using System.Net;
using System.Net.Mail;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;

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
        using var message = new MailMessage
        {
            From = new MailAddress(SenderAddress, "Quotix Feedback", Encoding.UTF8),
            Subject = $"[Quotix Bug&反馈] {request.ProblemType}",
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false,
            Body = BuildBody(request)
        };
        message.To.Add(RecipientAddress);

        AddAttachmentIfExists(message, request.ScreenshotPath);
        if (request.AttachErrorLog)
            AddAttachmentIfExists(message, request.ErrorLogPath);

        using var client = new SmtpClient(SmtpHost, SmtpPort)
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(SenderAddress, SenderPassword),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 30000
        };

        await client.SendMailAsync(message);
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

    private static void AddAttachmentIfExists(MailMessage message, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        message.Attachments.Add(new Attachment(path));
    }
}

/// <summary>问题反馈内容。</summary>
public sealed class FeedbackRequest
{
    public string ProblemType { get; init; } = "";
    public string Description { get; init; } = "";
    public string? ScreenshotPath { get; init; }
    public bool AttachErrorLog { get; init; }
    public string? ErrorLogPath { get; init; }

    public IReadOnlyList<string> AttachmentNames
    {
        get
        {
            var names = new List<string>();
            if (!string.IsNullOrWhiteSpace(ScreenshotPath) && File.Exists(ScreenshotPath))
                names.Add(Path.GetFileName(ScreenshotPath));
            if (AttachErrorLog && !string.IsNullOrWhiteSpace(ErrorLogPath) && File.Exists(ErrorLogPath))
                names.Add(Path.GetFileName(ErrorLogPath));
            return names;
        }
    }
}
