namespace PSMPE.Portal.Application.Common.Interfaces;

/// <summary>
/// Abstraction over how emails actually get sent - deliberately isolated so a real SMTP/SendGrid
/// implementation can be swapped in later (once credentials exist) without touching callers. See
/// openspecs/auth.md.
/// </summary>
public interface IEmailSender
{
    Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
