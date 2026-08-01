namespace PSMPE.Portal.Application.Common.Interfaces;

/// <summary>A single file attached to an outgoing email - e.g. the JPEG approval receipt.</summary>
public record EmailAttachment(string FileName, byte[] Content, string ContentType);

/// <summary>
/// Abstraction over how emails actually get sent - deliberately isolated so a real SMTP/SendGrid
/// implementation can be swapped in later (once credentials exist) without touching callers. See
/// openspecs/auth.md.
/// </summary>
public interface IEmailSender
{
    Task SendEmailAsync(
        string to,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default,
        IReadOnlyList<EmailAttachment>? attachments = null);
}
