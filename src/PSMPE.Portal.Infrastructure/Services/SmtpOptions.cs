namespace PSMPE.Portal.Infrastructure.Services;

/// <summary>
/// Binds the "Smtp" configuration section (appsettings.json / .env's Smtp__* keys). Deliberately
/// provider-agnostic - the same options shape works for SendGrid's SMTP relay, Mailgun, Amazon
/// SES SMTP, or a plain Gmail/Workspace account, so switching providers is a config change only.
/// See SmtpEmailSender and DependencyInjection.AddInfrastructure.
/// </summary>
public class SmtpOptions
{
      public const string SectionName = "Smtp";

      public string Host { get; set; } = string.Empty;
      public int Port { get; set; } = 587;
      public bool EnableSsl { get; set; } = true;
      public string Username { get; set; } = string.Empty;
      public string Password { get; set; } = string.Empty;
      public string FromEmail { get; set; } = string.Empty;
      public string FromName { get; set; } = "PSMPE Portal";
}
