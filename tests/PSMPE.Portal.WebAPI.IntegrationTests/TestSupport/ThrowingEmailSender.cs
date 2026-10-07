using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;

/// <summary>An IEmailSender that always fails, standing in for an SMTP outage (e.g. an exhausted
/// provider quota) - the failure mode the auth endpoints must report cleanly rather than as a 500.</summary>
public class ThrowingEmailSender : IEmailSender
{
    public Task SendEmailAsync(
        string to,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default,
        IReadOnlyList<EmailAttachment>? attachments = null) =>
        throw new InvalidOperationException("Simulated SMTP outage.");
}
