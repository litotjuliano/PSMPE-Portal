using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application.Common.Configuration;
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

        var member = BuildSeededMember(
            user.Id, await NextMembershipNoAsync(db), "Demo", "Member", Chapters.Ncr, "MP-100000");
        db.Members.Add(member);
        db.Payments.Add(BuildSettledRegistrationPayment(member));

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

        var membershipNo = await NextMembershipNoAsync(db);
        var member = BuildSeededMember(
            user.Id, membershipNo, firstName, lastName, chapter, $"MP-{membershipNo}");
        db.Members.Add(member);
        db.Payments.Add(BuildSettledRegistrationPayment(member));

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded approved Member profile for {Email}", email);
    }

    /// <summary>
    /// A seeded member in a state the services would actually produce: RMP licence present and
    /// verified, approved, Active, with a renewal date.
    ///
    /// Seeders write entities directly, so they bypass ApproveAsync's RMP and payment requirements.
    /// Left unchecked that produced demo data contradicting all three rules at once - approved
    /// without a licence, Active without a payment - which made the approval wizard render blank
    /// fields and made the RMP queue list every seeded member forever.
    /// </summary>
    private static Member BuildSeededMember(
        Guid userId, string membershipNo, string firstName, string lastName, string chapter, string prcLicenseNo)
    {
        var approvedAt = DateTimeOffset.UtcNow;
        return new Member
        {
            UserId = userId,
            MembershipNo = membershipNo,
            FirstName = firstName,
            LastName = lastName,
            Chapter = chapter,
            MemberType = MemberTypes.Regular,
            PrcLicenseNo = prcLicenseNo,
            PrcRegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2)),
            PrcValidUntilDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            PrcIdVerified = true,
            Status = MembershipStatus.Active,
            ApprovedAt = approvedAt,
            SubmittedAt = approvedAt,
            // Matches what PaymentVerification.Apply would have computed for the payment below.
            RenewalDueDate = DateOnly.FromDateTime(approvedAt.UtcDateTime).AddYears(1)
        };
    }

    /// <summary>The verified registration payment that backs a seeded member's Active status, so
    /// seed data doesn't show a membership nobody paid for.</summary>
    private static Payment BuildSettledRegistrationPayment(Member member) => new()
    {
        MemberId = member.Id,
        Kind = PaymentKind.NewMembership,
        Amount = MembershipFeeKeys.DefaultMembershipFee + MembershipFeeKeys.DefaultShippingFee,
        ReferenceNo = "SEED-" + member.MembershipNo,
        PaidOn = DateOnly.FromDateTime(member.ApprovedAt!.Value.UtcDateTime),
        // No real file behind this - seeded proof is a placeholder key, and the queue never asks
        // for it because the payment is already Verified.
        ProofStorageKey = $"seed/{member.MembershipNo}-proof.jpg",
        Status = PaymentStatus.Verified,
        DecidedAt = member.ApprovedAt,
        CoversUntil = member.RenewalDueDate
    };

    private static async Task<string> NextMembershipNoAsync(ApplicationDbContext db)
    {
        var existingNumbers = await db.Members.Select(m => m.MembershipNo).ToListAsync();
        var maxNumber = existingNumbers
            .Select(no => int.TryParse(no, out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max();
        return (maxNumber + 1).ToString("D6");
    }
}
