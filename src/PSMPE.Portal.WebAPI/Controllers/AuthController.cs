using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PSMPE.Portal.Application.Auth;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.WebAPI.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IJwtTokenGenerator jwtTokenGenerator,
    IEmailSender emailSender,
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

    /// <summary>
    /// Revision of the data privacy consent wording (RA 10173) currently shown on the sign-up
    /// form. Stamped onto the user alongside the timestamp so that changing the text later can't
    /// silently pass off old consent as agreement to new terms. Bump this whenever the wording in
    /// RegisterPage.tsx changes, and keep the two in step.
    /// </summary>
    private const string DataPrivacyConsentVersion = "2026-08-01";

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
            DataPrivacyConsentVersion = DataPrivacyConsentVersion,
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
    public async Task<ActionResult<ResendVerificationEmailResponse>> ResendVerificationEmail(ResendVerificationEmailRequest request)
    {
        const string genericMessage = "If an account with that email exists and isn't yet verified, a new verification email has been sent.";

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || user.EmailConfirmed)
        {
            // Don't reveal whether the account exists or is already verified.
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

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetLink = BuildResetPasswordLink(user.Id, token);
        await emailSender.SendEmailAsync(
            user.Email!,
            "Reset your PSMPE Portal password",
            $"<p>We received a request to reset your PSMPE Portal password. Click the link below to choose a new one:</p><p><a href=\"{resetLink}\">{resetLink}</a></p><p>If you didn't request this, you can safely ignore this email.</p>");

        return Ok(new ForgotPasswordResponse(genericMessage, ShowDevVerificationLink ? resetLink : null));
    }

    [HttpPost("reset-password")]
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

        // No JWT issued here - the user logs in fresh at /login rather than being silently
        // authenticated by whoever holds the link (see ForgotPasswordPage/ResetPasswordPage).
        return Ok(new ResetPasswordResponse("Your password has been reset. You can now sign in."));
    }

    [HttpGet("username-available")]
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
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

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
