namespace PSMPE.Portal.Application.Auth;

/// <summary>Username is optional - omitting it preserves the existing "UserName mirrors Email" behavior.</summary>
public record RegisterRequest(string Email, string Password, string DisplayName, string? Username = null);

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
