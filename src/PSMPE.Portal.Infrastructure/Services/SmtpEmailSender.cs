using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Infrastructure.Services;

/// <summary>
/// Real IEmailSender implementation, used once Smtp:Host is configured (see
/// DependencyInjection.AddInfrastructure) - falls back to ConsoleEmailSender otherwise so local
/// dev keeps working without real credentials.
/// </summary>
public class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var host = configuration["Smtp:Host"] ?? throw new InvalidOperationException("Smtp:Host is not configured.");
        var port = configuration.GetValue<int?>("Smtp:Port") ?? 587;
        var username = configuration["Smtp:Username"];
        var password = configuration["Smtp:Password"];
        var from = configuration["Smtp:From"] ?? throw new InvalidOperationException("Smtp:From is not configured.");
        var fromName = configuration["Smtp:FromName"] ?? "PSMPE Portal";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, from));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        // Port 465 is implicit TLS; everything else (587, 25) negotiates TLS via STARTTLS.
        var socketOptions = port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(host, port, socketOptions, cancellationToken);

        if (!string.IsNullOrWhiteSpace(username))
        {
            await client.AuthenticateAsync(username, password ?? string.Empty, cancellationToken);
        }

        try
        {
            await client.SendAsync(message, cancellationToken);
        }
        finally
        {
            await client.DisconnectAsync(true, cancellationToken);
        }
    }
}
