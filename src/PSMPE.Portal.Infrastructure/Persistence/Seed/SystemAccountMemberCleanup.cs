using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Infrastructure.Persistence.Seed;

/// <summary>
/// Removes any Member row owned by an administrative account (Super Admin, Admin, Manager, or
/// Accounts) - these roles don't have membership profiles (see MembersController), so any such
/// row is leftover data from a since-fixed bug where PUT /api/members/me had no role check and
/// let any authenticated account self-register as a Member. Runs unconditionally on every
/// startup (not gated by Seed:Enabled) so it self-heals any environment, including one where the
/// bug already ran before this fix shipped. Idempotent: a no-op once the data is clean.
/// </summary>
public static class SystemAccountMemberCleanup
{
    public static async Task CleanupAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ILogger logger)
    {
        var systemAccountUserIds = new HashSet<Guid>();
        foreach (var role in RoleNames.All.Where(r => r != RoleNames.Member))
        {
            foreach (var user in await userManager.GetUsersInRoleAsync(role))
            {
                systemAccountUserIds.Add(user.Id);
            }
        }

        if (systemAccountUserIds.Count == 0)
        {
            return;
        }

        var badMembers = await db.Members
            .Where(m => systemAccountUserIds.Contains(m.UserId))
            .Include(m => m.User)
            .ToListAsync();

        if (badMembers.Count == 0)
        {
            return;
        }

        foreach (var member in badMembers)
        {
            logger.LogWarning(
                "Removing Member profile '{MembershipNo}' from administrative account {Email} - administrative accounts do not have membership profiles.",
                member.MembershipNo, member.User.Email);
        }

        db.Members.RemoveRange(badMembers);
        await db.SaveChangesAsync();
    }
}
