using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotently creates a Member profile for the demo "member@psmpe.local" seed account (see
/// IdentitySeeder.RoleSeedUsers), so the dev credential cheatsheet / My Profile page has real
/// data to show. No-ops if that account doesn't exist yet (SEED_DEFAULT_PASSWORD unset) or
/// already has a Member profile.
/// </summary>
public static class MemberSeeder
{
    private const string DemoMemberEmail = "member@psmpe.local";

    public static async Task SeedAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ILogger logger)
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
            MembershipNo = "000001",
            FirstName = "Demo",
            LastName = "Member",
            Chapter = Chapters.Ncr,
            Status = MembershipStatus.Active,
            RenewalDueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6))
        });

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded Member profile for {Email}", DemoMemberEmail);
    }
}
