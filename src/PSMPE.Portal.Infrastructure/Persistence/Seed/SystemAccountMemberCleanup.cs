using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Infrastructure.Persistence.Seed;

/// <summary>
/// Removes any Member row owned by a staff-only account (an administrative role - Super Admin, Admin, Manager, or
/// Accounts - and no Member role). Staff-only accounts do not have membership profiles (see MembersController), so any such
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

        // Admin + Member is a real member who also has admin access, not leftover data.
        foreach (var user in await userManager.GetUsersInRoleAsync(RoleNames.Member))
        {
            systemAccountUserIds.Remove(user.Id);
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

        // A profile that already has payments is a financial record: deleting it violates
        // FK_Payments_Members_MemberId and, because this runs at startup, crash-loops the whole API.
        // Keep it and flag it for an admin to resolve instead of failing boot.
        var badMemberIds = badMembers.Select(m => m.Id).ToList();
        var memberIdsWithPayments = (await db.Payments
                .Where(p => badMemberIds.Contains(p.MemberId))
                .Select(p => p.MemberId)
                .Distinct()
                .ToListAsync())
            .ToHashSet();

        var removable = new List<Member>();
        foreach (var member in badMembers)
        {
            if (memberIdsWithPayments.Contains(member.Id))
            {
                logger.LogWarning(
                    "Keeping Member profile '{MembershipNo}' on administrative account {Email} - it has payments and cannot be removed automatically. Review it manually.",
                    member.MembershipNo, member.User.Email);
                continue;
            }

            logger.LogWarning(
                "Removing Member profile '{MembershipNo}' from administrative account {Email} - administrative accounts do not have membership profiles.",
                member.MembershipNo, member.User.Email);
            removable.Add(member);
        }

        if (removable.Count == 0)
        {
            return;
        }

        db.Members.RemoveRange(removable);
        await db.SaveChangesAsync();
    }
}
