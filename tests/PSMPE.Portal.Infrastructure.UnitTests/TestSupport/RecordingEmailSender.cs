using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Infrastructure.UnitTests.TestSupport;

/// <summary>Records every send instead of actually delivering anything, so a test can assert what
/// was sent (and to whom) without a real SMTP server.</summary>
public class RecordingEmailSender : IEmailSender
{
    public List<(string To, string Subject, string HtmlBody)> Sent { get; } = [];

    private readonly HashSet<string> _throwFor = [];

    public void ThrowWhenSendingTo(string to) => _throwFor.Add(to);

    public Task SendEmailAsync(
        string to, string subject, string htmlBody, CancellationToken cancellationToken = default,
        IReadOnlyList<EmailAttachment>? attachments = null)
    {
        if (_throwFor.Contains(to))
        {
            throw new InvalidOperationException($"Simulated send failure for {to}.");
        }

        Sent.Add((to, subject, htmlBody));
        return Task.CompletedTask;
    }
}
