using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Infrastructure.Services;

/// <summary>
/// Real IEmailSender implementation backed by a generic SMTP relay - which provider it actually
/// talks to (SendGrid's SMTP relay, Mailgun, Amazon SES SMTP, Gmail/Workspace, etc.) is entirely a
/// matter of the Smtp:* configuration (host/port/credentials), so switching providers later never
/// requires a code change. Registered instead of ConsoleEmailSender once Smtp:Host is configured -
/// see DependencyInjection.AddInfrastructure. Uses the built-in System.Net.Mail.SmtpClient rather
/// than a third-party package to avoid adding a new dependency for something this small; revisit
/// if we outgrow it (e.g. need provider-specific features like SendGrid's template API).
/// </summary>
public class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
private readonly SmtpOptions _options = options.Value;

public async Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
{
using var message = new MailMessage
{
From = new MailAddress(_options.FromEmail, _options.FromName),
Subject = subject,
Body = htmlBody,
IsBodyHtml = true
};
message.To.Add(to);

using var client = new SmtpClient(_options.Host, _options.Port)
{
EnableSsl = _options.EnableSsl,
Credentials = new NetworkCredential(_options.Username, _options.Password)
};

try
{
await client.SendMailAsync(message, cancellationToken);
logger.LogInformation("Email sent to {To}: {Subject}", to, subject);
}
catch (Exception ex)
{
logger.LogError(ex, "Failed to send email to {To} via {Host}:{Port}", to, _options.Host, _options.Port);
throw;
}
}
}
