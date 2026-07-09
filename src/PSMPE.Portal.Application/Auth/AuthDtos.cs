namespace PSMPE.Portal.Application.Auth;

public record RegisterRequest(string Email, string Password, string DisplayName);

public record LoginRequest(string Email, string Password);

public record AuthResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles);
