using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PSMPE.Portal.Application.Auth;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.WebAPI.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<ApplicationUser> userManager,
    IJwtTokenGenerator jwtTokenGenerator) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        // TODO: gate this behind the seeded SystemConfig "AllowPublicRegistration" flag once an
        // admin-facing settings UI exists to toggle it.
        var existing = await userManager.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            return Conflict(new { message = "An account with this email already exists." });
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description })));
        }

        // New self-registrations get the lowest-privilege role; Super Admin/Admin are granted by an existing admin.
        await userManager.AddToRoleAsync(user, RoleNames.ContentCreator);

        var roles = await userManager.GetRolesAsync(user);
        var (token, expiresAt) = jwtTokenGenerator.GenerateToken(user, roles);
        return Ok(new AuthResponse(token, expiresAt, user.Email!, user.DisplayName, roles.ToList()));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var roles = await userManager.GetRolesAsync(user);
        var (token, expiresAt) = jwtTokenGenerator.GenerateToken(user, roles);
        return Ok(new AuthResponse(token, expiresAt, user.Email!, user.DisplayName, roles.ToList()));
    }
}
