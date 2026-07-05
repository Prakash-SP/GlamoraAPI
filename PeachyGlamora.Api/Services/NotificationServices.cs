using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace PeachyGlamora.Api.Services;

public interface IEmailService
{
    Task SendAsync(string toEmail, string subject, string htmlBody);
}

/// <summary>Plain SMTP sender via MailKit — works with SendGrid, Amazon SES, Mailgun, or a
/// regular Gmail/Zoho SMTP relay just by changing appsettings. Swap for a provider SDK later
/// (SendGrid's HTTP API is faster/more reliable at volume) without touching callers.</summary>
public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_config["Smtp:FromName"] ?? "Peachy Glamora", _config["Smtp:FromEmail"]));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(_config["Smtp:Host"], int.Parse(_config["Smtp:Port"] ?? "587"), SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_config["Smtp:User"], _config["Smtp:Password"]);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            // A failed email should never break or roll back checkout — the order is already
            // placed and paid for. Log it so ops can see delivery failures and resend manually.
            _logger.LogError(ex, "Failed to send email to {Email} — subject: {Subject}", toEmail, subject);
        }
    }
}

public interface ISmsService
{
    Task SendSmsAsync(string phoneNumber, string message);
}
