using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotently seeds the fixed role set and a default Super Admin account.
/// Runs on startup, gated by the "Seed:Enabled" configuration flag.
/// </summary>
public static class IdentitySeeder
{
    /// <summary>
    /// Default permission grants applied only when a role is first created (see the loop below) -
    /// re-running the seeder never clobbers permissions an admin edits later via /admin/roles.
    /// Super Admin is the one exception: AdminController.GetRoles excludes it from that same UI
    /// ("isn't manageable through the app for any caller"), so it is never a customization surface
    /// an admin might have deliberately narrowed - unlike every other role, its claims are always
    /// reconciled up to exactly Permissions.All on every startup (see the loop below), so a
    /// permission added to Permissions.* after this role already exists in a given environment
    /// (e.g. Events.* did, on an environment seeded before that feature shipped) still reaches it.
    /// </summary>
    private static readonly Dictionary<string, string[]> DefaultRolePermissions = new()
    {
        [RoleNames.SuperAdmin] = Permissions.All,
        [RoleNames.Admin] =
        [
            Permissions.Content.Create, Permissions.Content.Update, Permissions.Content.Delete, Permissions.Content.ManageOthers,
            Permissions.Layout.Create, Permissions.Layout.Delete,
            Permissions.Admin.ManageUsers,
            Permissions.Ai.UsePrompt,
            Permissions.Members.View, Permissions.Members.Manage,
            Permissions.Events.View, Permissions.Events.Manage
        ],
        [RoleNames.Manager] =
        [
            Permissions.Content.Create, Permissions.Content.Update, Permissions.Content.Delete,
            Permissions.Layout.Create,
            Permissions.Ai.UsePrompt,
            Permissions.Members.View,
            Permissions.Events.View
        ],
        [RoleNames.Accounts] =
        [
            Permissions.Content.Update,
            Permissions.Ai.UsePrompt,
            Permissions.Members.View
        ],
        [RoleNames.Approval] =
        [
            Permissions.Members.View,
            Permissions.Members.Approve
        ],
        [RoleNames.Member] =
        [
            Permissions.Content.Create, Permissions.Content.Update
        ],
    };

    /// <summary>
    /// One demo account per non-Super-Admin role, created only when SEED_DEFAULT_PASSWORD is set
    /// (dev/Testing environments) - lets the login page's dev-only credential cheatsheet offer a
    /// working account for every role. Kept in sync with the frontend list in
    /// apps/web/src/integrations/template/pages/LoginPage.tsx (DEV_SEED_ACCOUNTS).
    /// </summary>
    private static readonly (string Role, string Email, string DisplayName)[] RoleSeedUsers =
    [
        (RoleNames.Admin, "admin.user@psmpe.local", "Demo Admin"),
        (RoleNames.Manager, "manager@psmpe.local", "Demo Manager"),
        (RoleNames.Accounts, "accounts@psmpe.local", "Demo Accounts"),
        (RoleNames.Approval, "approval@psmpe.local", "Demo Approval"),
        (RoleNames.Member, "member@psmpe.local", "Demo Member"),
    ];

    public static async Task SeedAsync(
        RoleManager<IdentityRole<Guid>> roleManager,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger logger)
    {
        foreach (var roleName in RoleNames.All)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                role = new IdentityRole<Guid>(roleName);
                await roleManager.CreateAsync(role);
                logger.LogInformation("Created role {RoleName}", roleName);

                if (DefaultRolePermissions.TryGetValue(roleName, out var permissions))
                {
                    foreach (var permission in permissions)
                    {
                        await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission));
                    }
                }
            }

            // See DefaultRolePermissions's doc comment: Super Admin is never a user-editable
            // customization surface, so (unlike every other role, seeded once above and left
            // alone) its claims are always reconciled up to the full permission set here -
            // additive only, so this is safe to run on every startup.
            if (roleName == RoleNames.SuperAdmin)
            {
                var existingPermissions = (await roleManager.GetClaimsAsync(role))
                    .Where(c => c.Type == Permissions.ClaimType)
                    .Select(c => c.Value)
                    .ToHashSet();
                foreach (var permission in Permissions.All)
                {
                    if (!existingPermissions.Contains(permission))
                    {
                        await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission));
                        logger.LogInformation("Granted missing permission {Permission} to {RoleName}", permission, roleName);
                    }
                }
            }
        }

        var adminEmail = configuration["SEED_ADMIN_EMAIL"];
        var adminPassword = configuration["SEED_ADMIN_PASSWORD"];

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            logger.LogWarning("SEED_ADMIN_EMAIL / SEED_ADMIN_PASSWORD not set — skipping default Super Admin seed.");
        }
        else
        {
            await SeedUserAsync(userManager, RoleNames.SuperAdmin, adminEmail, "System Administrator", adminPassword, logger);
        }

        var defaultPassword = configuration["SEED_DEFAULT_PASSWORD"];
        if (string.IsNullOrWhiteSpace(defaultPassword))
        {
            logger.LogWarning("SEED_DEFAULT_PASSWORD not set — skipping per-role demo account seed.");
            return;
        }

        foreach (var (role, email, displayName) in RoleSeedUsers)
        {
            await SeedUserAsync(userManager, role, email, displayName, defaultPassword, logger);
        }
    }

    private static async Task SeedUserAsync(
        UserManager<ApplicationUser> userManager,
        string role,
        string email,
        string displayName,
        string password,
        ILogger logger)
    {
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogError(
                "Failed to seed {Role} account {Email}: {Errors}",
                role, email, string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(user, role);
        logger.LogInformation("Seeded {Role} account {Email}", role, email);
    }
}
