namespace PSMPE.Portal.Application.Account;

/// <summary>
/// What an account may change about itself. Email is deliberately absent - it is the login
/// identifier, so changing it stays an administrative action (see AdminController.UpdateUser).
/// </summary>
public record UpdateAccountRequest(string DisplayName);

/// <summary>
/// Returned by the update so the client can refresh its cached copy without a second request.
/// Email and roles are read-only here; they are included because the web client caches the whole
/// user object at sign-in and would otherwise hold a stale display name until the token expires.
/// </summary>
public record AccountDto(string Email, string DisplayName, IReadOnlyList<string> Roles);

/// <summary>
/// The current password is required, not optional: it is what stops a stolen but unexpired token
/// from being escalated into a permanent account takeover.
/// </summary>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
