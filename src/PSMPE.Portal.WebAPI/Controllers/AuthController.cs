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
            DisplayName = request.DisplayName
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
