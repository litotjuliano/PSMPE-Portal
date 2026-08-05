namespace PSMPE.Portal.WebAPI.Extensions;

/// <summary>
/// Builds the emailed links that open the frontend app rather than the API. Shared because two
/// controllers now send password reset mail - AuthController for self-service forgot-password, and
/// AdminController when an administrator helps someone back into their account. A second copy of
/// these URLs would be a copy that has to stay in step with the frontend's routes forever; the
/// first time they drift, the emailed link 404s and the only symptom is a user who cannot get in.
/// </summary>
public static class AuthLinks
{
    private const string DefaultFrontendBaseUrl = "http://localhost:5173";

    private static string BaseUrl(IConfiguration configuration) =>
        configuration["Frontend:BaseUrl"] ?? DefaultFrontendBaseUrl;

    /// <summary>Matches the frontend's /verify-email route (see router.tsx).</summary>
    public static string VerifyEmail(IConfiguration configuration, Guid userId, string token) =>
        $"{BaseUrl(configuration)}/verify-email?userId={userId}&token={Uri.EscapeDataString(token)}";

    /// <summary>Matches the frontend's /reset-password route (see router.tsx).</summary>
    public static string ResetPassword(IConfiguration configuration, Guid userId, string token) =>
        $"{BaseUrl(configuration)}/reset-password?userId={userId}&token={Uri.EscapeDataString(token)}";
}
