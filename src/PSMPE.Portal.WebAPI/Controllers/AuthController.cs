using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PSMPE.Portal.Application.Auth;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.WebAPI.Extensions;

namespace PSMPE.Portal.WebAPI.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IJwtTokenGenerator jwtTokenGenerator,
    IEmailSender emailSender,
    IEmailSendThrottle emailSendThrottle,
    IConfiguration configuration,
    IWebHostEnvironment env) : ControllerBase
{
    /// <summary>Verification links must be exercisable without a real email provider - see
    /// ConsoleEmailSender's Open Questions. !IsProduction (not IsDevelopment) so this also
    /// applies under the "Testing" environment CustomWebApplicationFactory uses.</summary>
    private bool ShowDevVerificationLink => !env.IsProduction();

    private string BuildVerificationLink(Guid userId, string token)
    {
        var frontendBaseUrl = configuration["Frontend:BaseUrl"] ?? "http://localhost:5173";
        var encodedToken = Uri.EscapeDataString(token);
        return $"{frontendBaseUrl}/verify-email?userId={userId}&token={encodedToken}";
    }

    private string BuildResetPasswordLink(Guid userId, string token)
    {
        var frontendBaseUrl = configuration["Frontend:BaseUrl"] ?? "http://localhost:5173";
        var encodedToken = Uri.EscapeDataString(token);
        return $"{frontendBaseUrl}/reset-password?userId={userId}&token={encodedToken}";
    }

    private static readonly HashSet<string> PasswordPolicyErrorCodes =
    [
        "PasswordTooShort",
        "PasswordRequiresDigit",
        "PasswordRequiresUpper",
        "PasswordRequiresLower",
        "PasswordRequiresNonAlphanumeric",
    ];

    private async Task<IList<string>> GetPermissionsAsync(IList<string> roles)
    {
        var permissions = new HashSet<string>();
        foreach (var roleName in roles)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                continue;
            }

            var claims = await roleManager.GetClaimsAsync(role);
            foreach (var claim in claims.Where(c => c.Type == Permissions.ClaimType))
            {
                permissions.Add(claim.Value);
            }
        }

        return permissions.ToList();
    }

    [HttpPost("register")]
    [EnableRateLimiting(RateLimitingServiceExtensions.AuthIpPolicy)]
    public async Task<ActionResult<RegisterResponse>> Register(RegisterRequest request)
    {
        // Checked before anything else: without consent there is no lawful basis to process the
        // rest of the payload, so we don't want it reaching a uniqueness lookup either.
        if (!request.DataPrivacyConsent)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(RegisterRequest.DataPrivacyConsent)] =
                    ["Data privacy consent is required to create an account."],
            }));
        }

        // TODO: gate this behind the seeded SystemConfig "AllowPublicRegistration" flag once an
        // admin-facing settings UI exists to toggle it.
        var existing = await userManager.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            return Conflict(new { message = "An account with this email already exists." });
        }

        var userName = request.Email;
        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            if (await userManager.FindByNameAsync(request.Username) is not null)
            {
                return Conflict(new { message = $"Username '{request.Username}' is already taken." });
            }

            userName = request.Username;
        }

        var user = new ApplicationUser
        {
            UserName = userName,
            Email = request.Email,
            DisplayName = request.DisplayName,
            // Server clock, not a client-supplied time - the record is only worth keeping if the
            // caller can't backdate it. Version comes from the constant above rather than the
            // request for the same reason.
            DataPrivacyConsentAt = DateTimeOffset.UtcNow,
            DataPrivacyConsentVersion = DataPrivacyConsent.CurrentVersion,
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description })));
        }

        // New self-registrations get the lowest-privilege role; higher roles are granted by an existing admin.
        await userManager.AddToRoleAsync(user, RoleNames.Member);

        var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var verificationLink = BuildVerificationLink(user.Id, confirmationToken);
        await emailSender.SendEmailAsync(
            user.Email!,
            "Verify your PSMPE Portal account",
            $"<p>Welcome to PSMPE Portal. Please verify your email by clicking the link below:</p><p><a href=\"{verificationLink}\">{verificationLink}</a></p>");

        // No token here - the account can't be used until the email is confirmed (see Login).
        return Ok(new RegisterResponse(
            user.Email!,
            "Account created. Please check your email to verify your account before signing in.",
            ShowDevVerificationLink ? verificationLink : null));
    }

    [HttpPost("verify-email")]
    [EnableRateLimiting(RateLimitingServiceExtensions.AuthIpPolicy)]
    public async Task<ActionResult<AuthResponse>> VerifyEmail(VerifyEmailRequest request)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            return BadRequest(new { message = "Invalid verification link." });
        }

        var result = await userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
        {
            return BadRequest(new { message = "This verification link is invalid or has expired." });
        }

        var roles = await userManager.GetRolesAsync(user);
        var permissions = await GetPermissionsAsync(roles);
        var (token, expiresAt) = jwtTokenGenerator.GenerateToken(user, roles, permissions);
        return Ok(new AuthResponse(token, expiresAt, user.Email!, user.DisplayName, roles.ToList()));
    }

    [HttpPost("resend-verification-email")]
    [EnableRateLimiting(RateLimitingServiceExtensions.AuthEmailSendPolicy)]
    public async Task<ActionResult<ResendVerificationEmailResponse>> ResendVerificationEmail(ResendVerificationEmailRequest request)
    {
        const string genericMessage = "If an account with that email exists and isn't yet verified, a new verification email has been sent.";

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || user.EmailConfirmed)
        {
            // Don't reveal whether the account exists or is already verified.
            return Ok(new ResendVerificationEmailResponse(genericMessage));
        }

        if (!emailSendThrottle.TryRecordSend(request.Email))
        {
            return Ok(new ResendVerificationEmailResponse(genericMessage));
        }

        var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var verificationLink = BuildVerificationLink(user.Id, confirmationToken);
        await emailSender.SendEmailAsync(
            user.Email!,
            "Verify your PSMPE Portal account",
            $"<p>Please verify your email by clicking the link below:</p><p><a href=\"{verificationLink}\">{verificationLink}</a></p>");

        return Ok(new ResendVerificationEmailResponse(genericMessage, ShowDevVerificationLink ? verificationLink : null));
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitingServiceExtensions.AuthEmailSendPolicy)]
    public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword(ForgotPasswordRequest request)
    {
        const string genericMessage = "If an account with that email exists, a password reset link has been sent.";

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.EmailConfirmed)
        {
            // Don't reveal whether the account exists, and don't let an unverified account
            // request a reset link before it's even confirmed - mirrors ResendVerificationEmail's
            // anti-enumeration pattern above.
            return Ok(new ForgotPasswordResponse(genericMessage));
        }

        if (!emailSendThrottle.TryRecordSend(request.Email))
        {
            // Same generic response as every other path here - a throttled caller must not be
            // able to tell they were throttled, let alone that the account exists.
            return Ok(new ForgotPasswordResponse(genericMessage));
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetLink = BuildResetPasswordLink(user.Id, token);
        await emailSender.SendEmailAsync(
            user.Email!,
            "Reset your PSMPE Portal password",
            $"<p>We received a request to reset your PSMPE Portal password. Click the link below to choose a new one:</p><p><a href=\"{resetLink}\">{resetLink}</a></p><p>If you didn't request this, you can safely ignore this email.</p>");

        return Ok(new ForgotPasswordResponse(genericMessage, ShowDevVerificationLink ? resetLink : null));
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitingServiceExtensions.AuthIpPolicy)]
    public async Task<ActionResult<ResetPasswordResponse>> ResetPassword(ResetPasswordRequest request)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            return BadRequest(new { message = "This password reset link is invalid or has expired." });
        }

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => !PasswordPolicyErrorCodes.Contains(e.Code)))
            {
                // A non-policy failure means the token itself was invalid/expired/already used -
                // give the generic message rather than exposing Identity's internal error codes.
                return BadRequest(new { message = "This password reset link is invalid or has expired." });
            }

            return ValidationProblem(new ValidationProblemDetails(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description })));
        }

        // Clear any lockout. Without this the two mechanisms combine into a dead end for the
        // members most likely to hit it: someone who forgot their password, failed five times,
        // then reset it correctly would still be refused with ACCOUNT_LOCKED and would
        // reasonably conclude the reset failed - burning their 3-per-hour reset email allowance
        // on retries that cannot help. Holding the emailed token already proves inbox control,
        // which is a stronger claim than the failed password attempts disproved.
        await userManager.ResetAccessFailedCountAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);

        // No JWT issued here - the user logs in fresh at /login rather than being silently
        // authenticated by whoever holds the link (see ForgotPasswordPage/ResetPasswordPage).
        return Ok(new ResetPasswordResponse("Your password has been reset. You can now sign in."));
    }

    /// <summary>
    /// Both consent endpoints resolve the caller from the JWT rather than a route/body id - a
    /// user may only read or give their own consent, so there is nothing to authorize beyond
    /// being signed in.
    /// </summary>
    private async Task<ApplicationUser?> GetCurrentUserAsync() =>
        await userManager.GetUserAsync(User);

    [Authorize]
    [HttpGet("me/data-privacy-consent")]
    public async Task<ActionResult<DataPrivacyConsentStatusResponse>> GetDataPrivacyConsent()
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            // Token is valid but the account is gone (deleted mid-session).
            return Unauthorized();
        }

        return Ok(new DataPrivacyConsentStatusResponse(
            DataPrivacyConsent.NeedsConsent(user.DataPrivacyConsentVersion),
            DataPrivacyConsent.CurrentVersion,
            user.DataPrivacyConsentVersion,
            user.DataPrivacyConsentAt));
    }

    [Authorize]
    [HttpPost("me/data-privacy-consent")]
    public async Task<ActionResult<DataPrivacyConsentStatusResponse>> GiveDataPrivacyConsent()
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized();
        }

        // No request body: consenting is always to the *current* wording at the server's clock.
        // Accepting a version from the caller would let them claim consent to text they never saw.
        user.DataPrivacyConsentAt = DateTimeOffset.UtcNow;
        user.DataPrivacyConsentVersion = DataPrivacyConsent.CurrentVersion;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description })));
        }

        return Ok(new DataPrivacyConsentStatusResponse(
            DataPrivacyConsent.NeedsConsent(user.DataPrivacyConsentVersion),
            DataPrivacyConsent.CurrentVersion,
            user.DataPrivacyConsentVersion,
            user.DataPrivacyConsentAt));
    }

    [HttpGet("username-available")]
    [EnableRateLimiting(RateLimitingServiceExtensions.UsernameProbePolicy)]
    public async Task<ActionResult<bool>> IsUsernameAvailable(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return Ok(false);
        }

        var existing = await userManager.FindByNameAsync(username);
        return Ok(existing is null);
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitingServiceExtensions.AuthIpPolicy)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        const string genericFailure = "Invalid email or password.";
        const string lockedMessage = "This account is temporarily locked after too many failed sign-in attempts. Please try again later.";

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            // Same response as a wrong password - never reveal whether the account exists.
            return Unauthorized(new { message = genericFailure });
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return StatusCode(403, new { message = lockedMessage, code = "ACCOUNT_LOCKED" });
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);

            // Re-checked immediately so the attempt that trips the threshold says so, rather
            // than returning a plain 401 and only reporting the lockout on the next try.
            if (await userManager.IsLockedOutAsync(user))
            {
                return StatusCode(403, new { message = lockedMessage, code = "ACCOUNT_LOCKED" });
            }

            return Unauthorized(new { message = genericFailure });
        }

        await userManager.ResetAccessFailedCountAsync(user);

        if (!user.EmailConfirmed)
        {
            return StatusCode(403, new { message = "Please verify your email before signing in.", code = "EMAIL_NOT_CONFIRMED" });
        }

        var roles = await userManager.GetRolesAsync(user);
        var permissions = await GetPermissionsAsync(roles);
        var (token, expiresAt) = jwtTokenGenerator.GenerateToken(user, roles, permissions);
        return Ok(new AuthResponse(token, expiresAt, user.Email!, user.DisplayName, roles.ToList()));
    }
}
