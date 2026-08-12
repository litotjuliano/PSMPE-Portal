using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application.Common.Configuration;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Application.Payments;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using SkiaSharp;

namespace PSMPE.Portal.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotently creates Member profiles for demo seed accounts, so the dev credential cheatsheet /
/// My Profile / Members / Membership Approvals pages have real data to show. The extra approved
/// accounts below are only seeded when SEED_DEFAULT_PASSWORD is set (dev/Testing) - same gate
/// IdentitySeeder uses for its per-role demo accounts.
///
/// Every artifact (Member row, Payment row, each MemberUpload kind, the approval receipt) has its
/// own existence check rather than one bundle-level guard, so re-running this on an existing
/// database - the normal case, since it runs on every startup - backfills whatever is still
/// missing instead of only ever applying to a brand new database.
/// </summary>
public static class MemberSeeder
{
    private const string DemoMemberEmail = "member@psmpe.local";

    /// <summary>The document kinds a real approved application would always have on file (see
    /// MembersController's me/photo, me/prc-id, etc. uploads) - seeded here as clearly-labelled
    /// placeholders so completeness reads 100% and every "View" control has something to show,
    /// same as MembersController.IssueApprovalReceiptAsync does for a real approval.</summary>
    private static readonly (UploadKind Kind, string Label)[] PlaceholderDocumentKinds =
    [
        (UploadKind.Photo, "Photo"),
        (UploadKind.PrcId, "RMP ID"),
        (UploadKind.ValidGovernmentId, "Valid Government ID"),
        (UploadKind.Signature, "Signature"),
        (UploadKind.ProofOfPayment, "Proof of Payment"),
    ];

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

    public static async Task SeedAsync(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IMemberService memberService,
        IMemberUploadService uploadService,
        IPaymentService paymentService,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await SeedDemoMemberAsync(db, userManager, memberService, uploadService, paymentService, logger, cancellationToken);

        var defaultPassword = configuration["SEED_DEFAULT_PASSWORD"];
        if (string.IsNullOrWhiteSpace(defaultPassword))
        {
            return;
        }

        foreach (var seed in ApprovedSeedMembers)
        {
            await SeedApprovedMemberAsync(
                db, userManager, memberService, uploadService, paymentService,
                seed.Email, seed.DisplayName, seed.FirstName, seed.LastName, seed.Chapter, defaultPassword, logger, cancellationToken);
        }
    }

    private static async Task SeedDemoMemberAsync(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IMemberService memberService,
        IMemberUploadService uploadService,
        IPaymentService paymentService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(DemoMemberEmail);
        if (user is null)
        {
            return;
        }

        var member = await EnsureMemberAsync(
            db, user.Id, await NextMembershipNoAsync(db), "Demo", "Member", Chapters.Ncr, "MP-100000", logger, cancellationToken);
        await EnsureDocumentsAsync(db, member, memberService, uploadService, paymentService, cancellationToken);
    }

    private static async Task SeedApprovedMemberAsync(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IMemberService memberService,
        IMemberUploadService uploadService,
        IPaymentService paymentService,
        string email,
        string displayName,
        string firstName,
        string lastName,
        string chapter,
        string password,
        ILogger logger,
        CancellationToken cancellationToken)
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

        var membershipNo = await NextMembershipNoAsync(db);
        var member = await EnsureMemberAsync(
            db, user.Id, membershipNo, firstName, lastName, chapter, $"MP-{membershipNo}", logger, cancellationToken);
        await EnsureDocumentsAsync(db, member, memberService, uploadService, paymentService, cancellationToken);
    }

    /// <summary>Fetches the existing Member/Payment for this user, creating whichever is missing -
    /// so a database that already has the Member row (every restart after the first) still gets a
    /// Payment row if that's somehow missing, without ever re-creating or resetting the Member.</summary>
    private static async Task<Member> EnsureMemberAsync(
        ApplicationDbContext db, Guid userId, string membershipNo, string firstName, string lastName,
        string chapter, string prcLicenseNo, ILogger logger, CancellationToken cancellationToken)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (member is null)
        {
            member = BuildSeededMember(userId, membershipNo, firstName, lastName, chapter, prcLicenseNo);
            db.Members.Add(member);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded Member profile {MembershipNo} ({FirstName} {LastName})", membershipNo, firstName, lastName);
        }

        var hasPayment = await db.Payments.AnyAsync(
            p => p.MemberId == member.Id && p.Kind == PaymentKind.NewMembership, cancellationToken);
        if (!hasPayment)
        {
            db.Payments.Add(BuildSettledRegistrationPayment(member));
            await db.SaveChangesAsync(cancellationToken);
        }

        return member;
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

    /// <summary>
    /// Backfills whichever of the 5 required documents and the approval receipt this member is
    /// still missing, uploading through the real IMemberUploadService/ReceiptGenerator paths (not
    /// direct EF inserts) so completeness, "View" previews and the receipt download all work
    /// exactly as they would for a genuinely approved application. No email is sent - these are
    /// fake @psmpe.local addresses nobody reads, and sending mail during app-startup seeding is a
    /// fragility worth avoiding.
    /// </summary>
    private static async Task EnsureDocumentsAsync(
        ApplicationDbContext db, Member member, IMemberService memberService,
        IMemberUploadService uploadService, IPaymentService paymentService, CancellationToken cancellationToken)
    {
        var existingKinds = await db.MemberUploads.AsNoTracking()
            .Where(u => u.UserId == member.UserId)
            .Select(u => u.Kind)
            .ToListAsync(cancellationToken);

        foreach (var (kind, label) in PlaceholderDocumentKinds)
        {
            if (existingKinds.Contains(kind))
            {
                continue;
            }

            var placeholder = BuildPlaceholderImage(label);
            await using var stream = new MemoryStream(placeholder);
            await uploadService.UploadAsync(member.UserId, kind, stream, "seed-placeholder.jpg", placeholder.Length, cancellationToken);
        }

        if (!existingKinds.Contains(UploadKind.Receipt))
        {
            var dto = await memberService.GetByIdAsync(member.Id, cancellationToken);
            if (dto is not null)
            {
                var fees = await paymentService.GetFeesAsync(cancellationToken);
                var receiptBytes = ReceiptGenerator.Generate(dto, fees);
                await using var stream = new MemoryStream(receiptBytes);
                await uploadService.UploadAsync(member.UserId, UploadKind.Receipt, stream, "receipt.jpg", receiptBytes.Length, cancellationToken);
            }
        }
    }

    /// <summary>Renders a plainly-labelled JPEG placeholder (light background, border, centered
    /// "SEED PLACEHOLDER" + the document label) - obviously fake at a glance, but a real image file
    /// so every preview/download control that reads it actually works.</summary>
    private static byte[] BuildPlaceholderImage(string label)
    {
        const int width = 800;
        const int height = 600;

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(0xF0, 0xF0, 0xF0));

        using var borderPaint = new SKPaint { Color = new SKColor(0xBB, 0xBB, 0xBB), StrokeWidth = 6, IsStroke = true };
        canvas.DrawRect(3, 3, width - 6, height - 6, borderPaint);

        using var titlePaint = new SKPaint
        {
            Color = new SKColor(0x99, 0x33, 0x33), TextSize = 40, IsAntialias = true, TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold),
        };
        using var labelPaint = new SKPaint
        {
            Color = SKColors.DimGray, TextSize = 28, IsAntialias = true, TextAlign = SKTextAlign.Center,
        };

        canvas.DrawText("SEED PLACEHOLDER", width / 2f, height / 2f - 20, titlePaint);
        canvas.DrawText(label, width / 2f, height / 2f + 30, labelPaint);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data.ToArray();
    }

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
