using PSMPE.Portal.Application.Common.Configuration;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Payments;
using PSMPE.Portal.Application.Payments.Dtos;
using PSMPE.Portal.Application.UnitTests.TestSupport;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Payments;

public class PaymentServiceTests
{
    private static async Task<Member> SeedApprovedMemberAsync(TestDbContext db, DateOnly? renewalDueDate = null)
    {
        var user = new ApplicationUser { UserName = $"{Guid.NewGuid()}@example.com", Email = $"{Guid.NewGuid()}@example.com" };
        db.Add(user);

        var member = new Member
        {
            UserId = user.Id,
            User = user,
            FirstName = "Juan",
            LastName = "Dela Cruz",
            Chapter = Chapters.Ncr,
            MemberType = MemberTypes.Regular,
            PrcLicenseNo = "MP-1",
            PrcIdVerified = true,
            Status = MembershipStatus.Pending,
            SubmittedAt = DateTimeOffset.UtcNow.AddDays(-10),
            ApprovedAt = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
            RenewalDueDate = renewalDueDate,
        };
        db.Members.Add(member);
        await db.SaveChangesAsync();
        return member;
    }

    private static async Task<Payment> SeedPaymentAsync(
        TestDbContext db, Member member, PaymentKind kind, string? proofKey = "proof/key.jpg", bool includesPortalAccess = false)
    {
        var payment = new Payment
        {
            MemberId = member.Id,
            Kind = kind,
            Amount = 1700m,
            PaidOn = DateOnly.FromDateTime(DateTime.UtcNow),
            ProofStorageKey = proofKey,
            Status = PaymentStatus.Submitted,
            IncludesPortalAccess = includesPortalAccess,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    [Fact]
    public async Task VerifyAsync_NewMembership_ActivatesAndSetsFirstDueDateOneYearAfterApproval()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var member = await SeedApprovedMemberAsync(db);
        var payment = await SeedPaymentAsync(db, member, PaymentKind.NewMembership);

        var result = await service.VerifyAsync(payment.Id, Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Equal(MembershipStatus.Active, member.Status);
        // Approved 2026-03-15, so the first dues fall due a year later - matching the receipt's
        // "payable one year after registration".
        Assert.Equal(new DateOnly(2027, 3, 15), member.RenewalDueDate);
        Assert.Equal(PaymentStatus.Verified, payment.Status);
        Assert.Equal(member.RenewalDueDate, payment.CoversUntil);
    }

    /// <summary>
    /// The decision that matters most: a late payment must not shift the anniversary. Paying two
    /// months after a 2026-01-31 due date still renews to 2027-01-31, not a year from today.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_Renewal_AdvancesFromPreviousDueDate_NotFromToday()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var previousDueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-2);
        var member = await SeedApprovedMemberAsync(db, previousDueDate);
        var payment = await SeedPaymentAsync(db, member, PaymentKind.Renewal);

        var result = await service.VerifyAsync(payment.Id, Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Equal(previousDueDate.AddYears(1), member.RenewalDueDate);
        Assert.NotEqual(DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1), member.RenewalDueDate);
        Assert.Equal(MembershipStatus.Active, member.Status);
    }

    [Fact]
    public async Task VerifyAsync_IsIdempotent_AndDoesNotAdvanceTheDueDateTwice()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var previousDueDate = new DateOnly(2026, 6, 1);
        var member = await SeedApprovedMemberAsync(db, previousDueDate);
        var payment = await SeedPaymentAsync(db, member, PaymentKind.Renewal);

        Assert.True((await service.VerifyAsync(payment.Id, Guid.NewGuid())).Succeeded);
        Assert.True((await service.VerifyAsync(payment.Id, Guid.NewGuid())).Succeeded);

        Assert.Equal(new DateOnly(2027, 6, 1), member.RenewalDueDate);
    }

    [Fact]
    public async Task VerifyAsync_WithIncludesPortalAccessTrue_GrantsMemberPortalAccess()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var member = await SeedApprovedMemberAsync(db);
        var payment = await SeedPaymentAsync(db, member, PaymentKind.NewMembership, includesPortalAccess: true);

        var result = await service.VerifyAsync(payment.Id, Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.True(member.HasPortalAccess);
    }

    [Fact]
    public async Task VerifyAsync_WithIncludesPortalAccessFalse_LeavesMemberWithoutPortalAccess()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var member = await SeedApprovedMemberAsync(db);
        var payment = await SeedPaymentAsync(db, member, PaymentKind.NewMembership, includesPortalAccess: false);

        var result = await service.VerifyAsync(payment.Id, Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.False(member.HasPortalAccess);
    }

    /// <summary>
    /// Portal access is recurring, not permanent - it reflects only the member's most recently
    /// verified payment. A renewal that omits the add-on must revoke access already granted by an
    /// earlier payment.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_RenewalOmittingPortalAccess_RevokesPreviouslyGrantedAccess()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var member = await SeedApprovedMemberAsync(db, new DateOnly(2026, 6, 1));
        member.HasPortalAccess = true;
        await db.SaveChangesAsync();
        var renewal = await SeedPaymentAsync(db, member, PaymentKind.Renewal, includesPortalAccess: false);

        var result = await service.VerifyAsync(renewal.Id, Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.False(member.HasPortalAccess);
    }

    [Fact]
    public async Task VerifyAsync_ForAnUnapprovedMember_IsRejected()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var member = await SeedApprovedMemberAsync(db);
        member.ApprovedAt = null;
        await db.SaveChangesAsync();
        var payment = await SeedPaymentAsync(db, member, PaymentKind.NewMembership);

        var result = await service.VerifyAsync(payment.Id, Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(MembershipStatus.Pending, member.Status);
        Assert.Null(member.RenewalDueDate);
    }

    [Fact]
    public async Task VerifyAsync_WithoutProof_IsRejected()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var member = await SeedApprovedMemberAsync(db);
        var payment = await SeedPaymentAsync(db, member, PaymentKind.NewMembership, proofKey: null);

        var result = await service.VerifyAsync(payment.Id, Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(MembershipStatus.Pending, member.Status);
    }

    [Fact]
    public async Task RejectAsync_RecordsTheReason_AndLeavesMembershipUntouched()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var previousDueDate = new DateOnly(2026, 6, 1);
        var member = await SeedApprovedMemberAsync(db, previousDueDate);
        var payment = await SeedPaymentAsync(db, member, PaymentKind.Renewal);

        var result = await service.RejectAsync(payment.Id, "Deposit slip is illegible", Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Equal(PaymentStatus.Rejected, payment.Status);
        Assert.Equal("Deposit slip is illegible", payment.RejectedReason);
        // A rejected renewal leaves the member exactly where they were - still owing.
        Assert.Equal(previousDueDate, member.RenewalDueDate);
        Assert.Equal(MembershipStatus.Pending, member.Status);
    }

    [Fact]
    public async Task RejectAsync_AfterVerification_IsRefused()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var member = await SeedApprovedMemberAsync(db, new DateOnly(2026, 6, 1));
        var payment = await SeedPaymentAsync(db, member, PaymentKind.Renewal);
        await service.VerifyAsync(payment.Id, Guid.NewGuid());

        var result = await service.RejectAsync(payment.Id, "Changed my mind", Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(PaymentStatus.Verified, payment.Status);
    }

    [Fact]
    public async Task SubmitAsync_WhileOneIsAwaitingVerification_IsRefused()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var member = await SeedApprovedMemberAsync(db, new DateOnly(2026, 6, 1));
        await SeedPaymentAsync(db, member, PaymentKind.Renewal);

        var result = await service.SubmitAsync(
            member.UserId, new SubmitPaymentRequest(600m, "REF-2", DateOnly.FromDateTime(DateTime.UtcNow)));

        // Two pending submissions would let an admin verify both and advance the due date twice for
        // one year's dues.
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(null, PaymentKind.NewMembership)]
    [InlineData("2026-06-01", PaymentKind.Renewal)]
    public async Task SubmitAsync_DerivesKindFromMembershipState(string? renewalDueDate, PaymentKind expected)
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var member = await SeedApprovedMemberAsync(db, renewalDueDate is null ? null : DateOnly.Parse(renewalDueDate));

        var result = await service.SubmitAsync(
            member.UserId, new SubmitPaymentRequest(1700m, "REF-1", DateOnly.FromDateTime(DateTime.UtcNow)));

        Assert.True(result.Succeeded);
        // Taken from the member's own state, never from the caller - otherwise a member could claim
        // a renewal for a membership that was never activated.
        Assert.Equal(expected, result.Value!.Kind);
    }

    [Fact]
    public async Task SubmitAsync_WithIncludePortalAccessTrue_SetsIncludesPortalAccessOnThePayment()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var member = await SeedApprovedMemberAsync(db, new DateOnly(2026, 6, 1));

        var result = await service.SubmitAsync(
            member.UserId, new SubmitPaymentRequest(1500m, "REF-1", DateOnly.FromDateTime(DateTime.UtcNow), IncludePortalAccess: true));

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.Id != Guid.Empty);
        var stored = await db.Payments.FindAsync(result.Value.Id);
        Assert.True(stored!.IncludesPortalAccess);
    }

    [Fact]
    public async Task SubmitAsync_WithoutIncludePortalAccess_DefaultsToFalseOnThePayment()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var member = await SeedApprovedMemberAsync(db, new DateOnly(2026, 6, 1));

        var result = await service.SubmitAsync(
            member.UserId, new SubmitPaymentRequest(600m, "REF-1", DateOnly.FromDateTime(DateTime.UtcNow)));

        Assert.True(result.Succeeded);
        var stored = await db.Payments.FindAsync(result.Value!.Id);
        Assert.False(stored!.IncludesPortalAccess);
    }

    [Fact]
    public async Task SubmitAsync_WithAFutureDate_IsRefused()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var member = await SeedApprovedMemberAsync(db);

        var result = await service.SubmitAsync(
            member.UserId, new SubmitPaymentRequest(1700m, "REF-1", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)));

        Assert.False(result.Succeeded);
    }

    /// <summary>
    /// A second payment must not make the first one's proof unreachable - the reason payments own
    /// their document instead of sharing MemberUpload's single (UserId, ProofOfPayment) slot.
    /// </summary>
    [Fact]
    public async Task EachPaymentKeepsItsOwnProof()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var member = await SeedApprovedMemberAsync(db, new DateOnly(2026, 6, 1));

        var first = await SeedPaymentAsync(db, member, PaymentKind.NewMembership, "proof/registration.jpg");
        await service.VerifyAsync(first.Id, Guid.NewGuid());
        var second = await SeedPaymentAsync(db, member, PaymentKind.Renewal, "proof/renewal.jpg");

        Assert.Equal("proof/registration.jpg", await service.GetProofKeyAsync(first.Id));
        Assert.Equal("proof/renewal.jpg", await service.GetProofKeyAsync(second.Id));
    }

    [Fact]
    public void MembershipFeesDto_ComputesAllFourTotalsCorrectly()
    {
        var fees = new MembershipFeesDto(MembershipFee: 1500m, ShippingFee: 200m, AnnualDues: 600m, PortalFee: 900m);

        Assert.Equal(1700m, fees.RegistrationTotalWithoutPortal);
        Assert.Equal(2600m, fees.RegistrationTotalWithPortal);
        Assert.Equal(600m, fees.RenewalTotalWithoutPortal);
        Assert.Equal(1500m, fees.RenewalTotalWithPortal);
    }

    [Fact]
    public async Task GetFeesAsync_WithNoConfigRows_FallsBackToTheShippedDefaults()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);

        var fees = await service.GetFeesAsync();

        // A database missing these rows must behave as before, not charge zero.
        Assert.Equal(MembershipFeeKeys.DefaultMembershipFee, fees.MembershipFee);
        Assert.Equal(MembershipFeeKeys.DefaultShippingFee, fees.ShippingFee);
        Assert.Equal(MembershipFeeKeys.DefaultAnnualDues, fees.AnnualDues);
        Assert.Equal(MembershipFeeKeys.DefaultPortalFee, fees.PortalFee);
        Assert.Equal(1700m, fees.RegistrationTotalWithoutPortal);
    }

    [Fact]
    public async Task UpdateFeesAsync_PersistsAndIsReadBack()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);

        Assert.True((await service.UpdateFeesAsync(new UpdateMembershipFeesRequest(2000m, 250m, 750m, 950m))).Succeeded);
        var fees = await service.GetFeesAsync();

        Assert.Equal(2000m, fees.MembershipFee);
        Assert.Equal(250m, fees.ShippingFee);
        Assert.Equal(750m, fees.AnnualDues);
        Assert.Equal(950m, fees.PortalFee);
        Assert.Equal(2250m, fees.RegistrationTotalWithoutPortal);
        Assert.Equal(3200m, fees.RegistrationTotalWithPortal);
        Assert.Equal(750m, fees.RenewalTotalWithoutPortal);
        Assert.Equal(1700m, fees.RenewalTotalWithPortal);
    }

    [Fact]
    public async Task UpdateFeesAsync_WithANegativeFee_IsRefused()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);

        var result = await service.UpdateFeesAsync(new UpdateMembershipFeesRequest(-1m, 200m, 600m, 900m));

        Assert.False(result.Succeeded);
    }

    /// <summary>
    /// The invariant the whole promotional-pricing/fee-editing plan depends on: amounts are
    /// captured once, at submission time. An admin editing fees afterward must not reach back and
    /// change what a member already submitted or had verified.
    /// </summary>
    [Fact]
    public async Task UpdateFeesAsync_DoesNotRetroactivelyChangeAnAlreadySubmittedPayment()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var member = await SeedApprovedMemberAsync(db);
        var payment = await SeedPaymentAsync(db, member, PaymentKind.NewMembership);
        var originalAmount = payment.Amount;
        var originalIncludesPortalAccess = payment.IncludesPortalAccess;

        Assert.True((await service.UpdateFeesAsync(new UpdateMembershipFeesRequest(2000m, 250m, 750m, 950m))).Succeeded);

        var reloaded = await db.Payments.FindAsync(payment.Id);
        Assert.Equal(originalAmount, reloaded!.Amount);
        // Task 3 wires this up for real; here it just needs to still be whatever it was before the
        // edit (the domain default), proving UpdateFeesAsync doesn't touch it at all.
        Assert.Equal(originalIncludesPortalAccess, reloaded.IncludesPortalAccess);
        Assert.False(reloaded.IncludesPortalAccess);
    }

    [Fact]
    public async Task CreatePromotionAsync_ActiveToday_IsReflectedInGetFeesAsync()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await service.CreatePromotionAsync(
            new CreateFeePromotionRequest(MembershipFeeKeys.MembershipFee, 999m, today, today.AddDays(1)),
            Guid.NewGuid());

        Assert.True(result.Succeeded);
        var fees = await service.GetFeesAsync();
        Assert.Equal(999m, fees.MembershipFee);
    }

    [Fact]
    public async Task CreatePromotionAsync_OutsideItsDateRange_DoesNotAffectGetFeesAsync()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await service.CreatePromotionAsync(
            new CreateFeePromotionRequest(MembershipFeeKeys.MembershipFee, 999m, today.AddDays(5), today.AddDays(10)),
            Guid.NewGuid());

        Assert.True(result.Succeeded);
        var fees = await service.GetFeesAsync();
        Assert.Equal(MembershipFeeKeys.DefaultMembershipFee, fees.MembershipFee);
    }

    [Fact]
    public async Task CreatePromotionAsync_OverlappingAnExistingPromotionForTheSameFeeKey_IsRejected()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.True((await service.CreatePromotionAsync(
            new CreateFeePromotionRequest(MembershipFeeKeys.MembershipFee, 999m, today, today.AddDays(10)),
            Guid.NewGuid())).Succeeded);

        // Overlaps by one day (today+5..today+15 vs today..today+10).
        var result = await service.CreatePromotionAsync(
            new CreateFeePromotionRequest(MembershipFeeKeys.MembershipFee, 800m, today.AddDays(5), today.AddDays(15)),
            Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task CreatePromotionAsync_NonOverlappingRangeForTheSameFeeKey_Succeeds()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.True((await service.CreatePromotionAsync(
            new CreateFeePromotionRequest(MembershipFeeKeys.MembershipFee, 999m, today, today.AddDays(10)),
            Guid.NewGuid())).Succeeded);

        var result = await service.CreatePromotionAsync(
            new CreateFeePromotionRequest(MembershipFeeKeys.MembershipFee, 800m, today.AddDays(11), today.AddDays(20)),
            Guid.NewGuid());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CreatePromotionAsync_ForAnUnrecognizedFeeKey_IsRejected()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await service.CreatePromotionAsync(
            new CreateFeePromotionRequest("NotARealFee", 500m, today, today.AddDays(1)), Guid.NewGuid());

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CreatePromotionAsync_WithStartDateAfterEndDate_IsRejected()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await service.CreatePromotionAsync(
            new CreateFeePromotionRequest(MembershipFeeKeys.MembershipFee, 500m, today.AddDays(5), today),
            Guid.NewGuid());

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task DeletePromotionAsync_RemovesIt_AndFeesRevertToRegular()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var created = await service.CreatePromotionAsync(
            new CreateFeePromotionRequest(MembershipFeeKeys.MembershipFee, 999m, today, today.AddDays(1)),
            Guid.NewGuid());
        Assert.Equal(999m, (await service.GetFeesAsync()).MembershipFee);

        var deleteResult = await service.DeletePromotionAsync(created.Value!.Id);

        Assert.True(deleteResult.Succeeded);
        Assert.Equal(MembershipFeeKeys.DefaultMembershipFee, (await service.GetFeesAsync()).MembershipFee);
        Assert.Empty(await service.GetPromotionsAsync());
    }

    [Fact]
    public async Task DeletePromotionAsync_ForAnUnknownId_ReturnsNotFound()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);

        var result = await service.DeletePromotionAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
    }

    private static async Task<(Member Member, EventRegistration Registration)> SeedEventRegistrationAsync(
        TestDbContext db, EventRegistrationStatus status = EventRegistrationStatus.Registered)
    {
        var user = new ApplicationUser { UserName = $"{Guid.NewGuid()}@example.com", Email = $"{Guid.NewGuid()}@example.com" };
        db.Add(user);
        var member = new Member { UserId = user.Id, User = user, FirstName = "Ana", LastName = "Reyes", Chapter = Chapters.Ncr, MemberType = MemberTypes.Regular };
        db.Members.Add(member);

        var @event = new Event { Title = "Seminar", StartsAt = DateTimeOffset.UtcNow.AddDays(5), EndsAt = DateTimeOffset.UtcNow.AddDays(5).AddHours(4), FeeOnsite = 500m, FeeOnline = 200m };
        db.Events.Add(@event);

        var registration = new EventRegistration { EventId = @event.Id, Event = @event, MemberId = member.Id, Member = member, Mode = EventMode.Onsite, Status = status };
        db.EventRegistrations.Add(registration);
        await db.SaveChangesAsync();
        return (member, registration);
    }

    [Fact]
    public async Task SubmitForEventRegistrationAsync_Valid_CreatesPaymentAndMovesToPaymentSubmitted()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (member, registration) = await SeedEventRegistrationAsync(db);

        var result = await service.SubmitForEventRegistrationAsync(
            member.UserId, registration.Id, new SubmitPaymentRequest(500m, "REF-1", DateOnly.FromDateTime(DateTime.UtcNow)));

        Assert.True(result.Succeeded);
        Assert.Equal(PaymentKind.EventRegistration, result.Value!.Kind);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.PaymentSubmitted, updated!.Status);
    }

    [Fact]
    public async Task SubmitForEventRegistrationAsync_SecondSubmissionWhilePending_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (member, registration) = await SeedEventRegistrationAsync(db);
        var request = new SubmitPaymentRequest(500m, "REF-1", DateOnly.FromDateTime(DateTime.UtcNow));
        await service.SubmitForEventRegistrationAsync(member.UserId, registration.Id, request);

        var result = await service.SubmitForEventRegistrationAsync(member.UserId, registration.Id, request);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
    }

    /// <summary>Matches spec.md's "Verifying an event payment advances the registration".</summary>
    [Fact]
    public async Task VerifyAsync_EventRegistrationPayment_MovesRegistrationToPaymentVerified()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (member, registration) = await SeedEventRegistrationAsync(db, EventRegistrationStatus.PaymentSubmitted);
        var payment = new Payment
        {
            MemberId = member.Id, Kind = PaymentKind.EventRegistration, EventRegistrationId = registration.Id,
            Amount = 500m, PaidOn = DateOnly.FromDateTime(DateTime.UtcNow), ProofStorageKey = "proof/key.jpg",
            Status = PaymentStatus.Submitted,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var result = await service.VerifyAsync(payment.Id, Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Equal(PaymentStatus.Verified, payment.Status);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.PaymentVerified, updated!.Status);
    }

    /// <summary>Matches spec.md's "A rejected event payment can be resubmitted".</summary>
    [Fact]
    public async Task RejectAsync_EventRegistrationPayment_SetsRegistrationRejectedAndAllowsResubmission()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (member, registration) = await SeedEventRegistrationAsync(db, EventRegistrationStatus.PaymentSubmitted);
        var payment = new Payment
        {
            MemberId = member.Id, Kind = PaymentKind.EventRegistration, EventRegistrationId = registration.Id,
            Amount = 500m, PaidOn = DateOnly.FromDateTime(DateTime.UtcNow), ProofStorageKey = "proof/key.jpg",
            Status = PaymentStatus.Submitted,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var rejectResult = await service.RejectAsync(payment.Id, "Amount doesn't match the fee.", Guid.NewGuid());

        Assert.True(rejectResult.Succeeded);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.Rejected, updated!.Status);

        var resubmit = await service.SubmitForEventRegistrationAsync(
            member.UserId, registration.Id, new SubmitPaymentRequest(500m, "REF-2", DateOnly.FromDateTime(DateTime.UtcNow)));
        Assert.True(resubmit.Succeeded);
    }

    /// <summary>Matches spec.md's "An admin records a cash payment".</summary>
    [Fact]
    public async Task RecordEventCashPaymentAsync_Valid_CreatesVerifiedPaymentAndMovesRegistration()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (_, registration) = await SeedEventRegistrationAsync(db);
        var adminUserId = Guid.NewGuid();

        var result = await service.RecordEventCashPaymentAsync(registration.Id, 500m, adminUserId);

        Assert.True(result.Succeeded);
        Assert.Equal(PaymentStatus.Verified, result.Value!.Status);
        Assert.False(result.Value.HasProof);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.PaymentVerified, updated!.Status);
    }

    /// <summary>Matches spec.md's "A cash payment cannot be recorded over an existing payment".</summary>
    [Fact]
    public async Task RecordEventCashPaymentAsync_RegistrationAlreadyHasSubmittedPayment_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (member, registration) = await SeedEventRegistrationAsync(db);
        await service.SubmitForEventRegistrationAsync(
            member.UserId, registration.Id, new SubmitPaymentRequest(500m, "REF-1", DateOnly.FromDateTime(DateTime.UtcNow)));

        var result = await service.RecordEventCashPaymentAsync(registration.Id, 500m, Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task RecordEventCashPaymentAsync_AfterEarlierRejection_Succeeds()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (member, registration) = await SeedEventRegistrationAsync(db);
        var submitted = await service.SubmitForEventRegistrationAsync(
            member.UserId, registration.Id, new SubmitPaymentRequest(500m, "REF-1", DateOnly.FromDateTime(DateTime.UtcNow)));
        await service.RejectAsync(submitted.Value!.Id, "Wrong amount.", Guid.NewGuid());

        var result = await service.RecordEventCashPaymentAsync(registration.Id, 500m, Guid.NewGuid());

        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// A registration cancelled before it ever had a payment (Registered -> Cancelled via
    /// CancelRegistrationAsync) has no Payment row at all, so the pre-existing hasActivePayment
    /// check alone would let this through and EventPaymentVerification.Apply would flip a
    /// Cancelled registration back to PaymentVerified - resurrecting it. Cancelled must stay a
    /// terminal off-ramp (see openspecs/events.md).
    /// </summary>
    [Fact]
    public async Task RecordEventCashPaymentAsync_OnACancelledRegistration_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (_, registration) = await SeedEventRegistrationAsync(db, EventRegistrationStatus.Cancelled);

        var result = await service.RecordEventCashPaymentAsync(registration.Id, 500m, Guid.NewGuid());

        Assert.False(result.Succeeded);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.Cancelled, updated!.Status);
    }
}
