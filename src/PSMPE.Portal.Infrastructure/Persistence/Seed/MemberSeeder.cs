using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotently creates Member profiles for demo seed accounts, so the dev credential cheatsheet /
/// My Profile / Members / Membership Approvals pages have real data to show. The extra approved
/// accounts below are only seeded when SEED_DEFAULT_PASSWORD is set (dev/Testing) - same gate
/// IdentitySeeder uses for its per-role demo accounts.
/// </summary>
public static class MemberSeeder
{
    private const string DemoMemberEmail = "member@psmpe.local";

    /// <summary>
    /// Dedicated login accounts (not in IdentitySeeder.RoleSeedUsers - those are one per role for
    /// permission testing) seeded here purely to back additional approved Member profiles, so
    /// admin screens like Members and Membership Approvals have more than one real row to show.
    /// </summary>
    private static readonly (string Email, string DisplayName, string FirstName, string LastName, string Chapter)[] ApprovedSeedMembers =
    [
        ("juan.delacruz@psmpe.local", "Juan Dela Cruz", "Juan", "Dela Cruz", Chapters.Ncr),
        ("maria.fernandez@psmpe.local", "Maria Fernandez", "Maria", "Fernandez", Chapters.Cebu),
        ("pedro.bautista@psmpe.local", "Pedro Bautista", "Pedro", "Bautista", Chapters.Davao),
    ];

    public static async Task SeedAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IConfiguration configuration, ILogger logger)
    {
        await SeedDemoMemberAsync(db, userManager, logger);

        var defaultPassword = configuration["SEED_DEFAULT_PASSWORD"];
        if (string.IsNullOrWhiteSpace(defaultPassword))
        {
            return;
        }

        foreach (var seed in ApprovedSeedMembers)
        {
            await SeedApprovedMemberAsync(db, userManager, seed.Email, seed.DisplayName, seed.FirstName, seed.LastName, seed.Chapter, defaultPassword, logger);
        }
    }

    private static async Task SeedDemoMemberAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ILogger logger)
    {
        var user = await userManager.FindByEmailAsync(DemoMemberEmail);
        if (user is null)
        {
            return;
        }

        if (await db.Members.AnyAsync(m => m.UserId == user.Id))
        {
            return;
        }

        db.Members.Add(new Member
        {
            UserId = user.Id,
            MembershipNo = await NextMembershipNoAsync(db),
            FirstName = "Demo",
            LastName = "Member",
            Chapter = Chapters.Ncr,
            MemberType = MemberTypes.Regular,
            Status = MembershipStatus.Active,
            ApprovedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            RenewalDueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6))
        });

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded Member profile for {Email}", DemoMemberEmail);
    }

    private static async Task SeedApprovedMemberAsync(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        string email,
        string displayName,
        string firstName,
        string lastName,
        string chapter,
        string password,
        ILogger logger)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
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
                    "Failed to seed approved-member account {Email}: {Errors}",
                    email, string.Join("; ", result.Errors.Select(e => e.Description)));
                return;
            }

            await userManager.AddToRoleAsync(user, RoleNames.Member);
            logger.LogInformation("Seeded Member account {Email}", email);
        }

        if (await db.Members.AnyAsync(m => m.UserId == user.Id))
        {
            return;
        }

        db.Members.Add(new Member
        {
            UserId = user.Id,
            MembershipNo = await NextMembershipNoAsync(db),
            FirstName = firstName,
            LastName = lastName,
            Chapter = chapter,
            MemberType = MemberTypes.Regular,
            Status = MembershipStatus.Active,
            ApprovedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            RenewalDueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6))
        });

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded approved Member profile for {Email}", email);
    }

    private static async Task<string> NextMembershipNoAsync(ApplicationDbContext db)
    {
        var count = await db.Members.CountAsync();
        return (count + 1).ToString("D6");
    }
}
