using System.Collections.Concurrent;
using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;

/// <summary>An IEmailSender that remembers who it was asked to email, so a test can assert on the
/// recipients without a real mail server.</summary>
public class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<string> _recipients = new();

    public IReadOnlyCollection<string> Recipients => _recipients.ToArray();

    public Task SendEmailAsync(
        string to,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default,
        IReadOnlyList<EmailAttachment>? attachments = null)
    {
        _recipients.Enqueue(to);
        return Task.CompletedTask;
    }
}
