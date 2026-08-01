namespace PSMPE.Portal.Application.Auth;

/// <summary>
/// Username is optional - omitting it preserves the existing "UserName mirrors Email" behavior.
/// DataPrivacyConsent must be true; registration is rejected otherwise, so the consent is a real
/// server-side gate rather than a checkbox the client could skip. It defaults to false so an
/// omitted field fails closed.
/// </summary>
public record RegisterRequest(
    string Email,
    string Password,
    string DisplayName,
    string? Username = null,
    bool DataPrivacyConsent = false);

public record LoginRequest(string Email, string Password);

public record AuthResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles);

/// <summary>
/// Registration no longer returns a usable token - the account can't be used until the email is
/// confirmed. DevVerificationLink is only populated outside Production, so the whole flow is
/// testable without a real email provider (see IEmailSender's Open Questions).
/// </summary>
public record RegisterResponse(string Email, string Message, string? DevVerificationLink = null);

public record VerifyEmailRequest(Guid UserId, string Token);

public record ResendVerificationEmailRequest(string Email);

public record ResendVerificationEmailResponse(string Message, string? DevVerificationLink = null);

public record ForgotPasswordRequest(string Email);

public record ForgotPasswordResponse(string Message, string? DevResetLink = null);

public record ResetPasswordRequest(Guid UserId, string Token, string NewPassword);

public record ResetPasswordResponse(string Message);

/// <summary>
/// Where the signed-in account stands on the data privacy notice. <c>NeedsConsent</c> is the
/// server's verdict rather than something the client derives, so bumping the wording version
/// takes effect without a frontend deploy. <c>ConsentedVersion</c>/<c>ConsentedAt</c> are null
/// for accounts that never consented (seeded/admin-created, or registered before it was recorded).
/// </summary>
public record DataPrivacyConsentStatusResponse(
    bool NeedsConsent,
    string CurrentVersion,
    string? ConsentedVersion,
    DateTimeOffset? ConsentedAt);
