using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Infrastructure.Services;

/// <summary>
/// Fallback IEmailSender used when Smtp:Host isn't configured (see
/// DependencyInjection.AddInfrastructure) - just logs the email instead of actually sending it.
/// Combined with AuthController exposing the verification link directly in non-Production
/// responses, this keeps the whole email-verification flow testable without real SMTP
/// credentials. See SmtpEmailSender for the real implementation.
/// </summary>
public class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Email (not actually sent - no provider configured) to {To}: {Subject}\n{Body}", to, subject, htmlBody);
        return Task.CompletedTask;
    }
}
