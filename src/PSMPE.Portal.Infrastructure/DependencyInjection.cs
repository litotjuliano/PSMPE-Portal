using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using PSMPE.Portal.Application.AI;
using PSMPE.Portal.Application.Auth;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Infrastructure.AI;
using PSMPE.Portal.Infrastructure.Authorization;
using PSMPE.Portal.Infrastructure.Authorization.Policies;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.Infrastructure.Services;

namespace PSMPE.Portal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                // Read Jwt:Key lazily (not captured before this delegate runs) so it reflects
                // configuration sources added after AddInfrastructure() is called, e.g. by
                // WebApplicationFactory in integration tests.
                var jwtKey = configuration["Jwt:Key"]
                    ?? throw new InvalidOperationException("Jwt:Key configuration value is missing.");

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration["Jwt:Issuer"] ?? "PSMPE.Portal",
                    ValidAudience = configuration["Jwt:Audience"] ?? "PSMPE.Portal.Client",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(PolicyNames.RequireAdmin, policy =>
                policy.RequireRole(Domain.Enums.RoleNames.Admin, Domain.Enums.RoleNames.SuperAdmin))
            .AddPolicy(PolicyNames.RequireSuperAdmin, policy =>
                policy.RequireRole(Domain.Enums.RoleNames.SuperAdmin))
            .AddPolicy(PolicyNames.ContentOwnerOrAdmin, policy =>
                policy.Requirements.Add(new OwnershipRequirement()));

        services.AddScoped<IAuthorizationHandler, OwnershipAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, SystemAdminAuthorizationHandler>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IPromptExecutionService, OpenAiPromptExecutionService>();

        return services;
    }
}
