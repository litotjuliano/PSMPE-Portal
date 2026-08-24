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
        TestDbContext db, Member member, PaymentKind kind, string? proofKey = "proof/key.jpg")
    {
        var payment = new Payment
        {
            MemberId = member.Id,
            Kind = kind,
            Amount = 1700m,
            PaidOn = DateOnly.FromDateTime(DateTime.UtcNow),
            ProofStorageKey = proofKey,
            Status = PaymentStatus.Submitted,
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
    public async Task GetFeesAsync_WithNoConfigRows_FallsBackToTheShippedDefaults()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);

        var fees = await service.GetFeesAsync();

        // A database missing these rows must behave as before, not charge zero.
        Assert.Equal(MembershipFeeKeys.DefaultMembershipFee, fees.MembershipFee);
        Assert.Equal(MembershipFeeKeys.DefaultShippingFee, fees.ShippingFee);
        Assert.Equal(MembershipFeeKeys.DefaultAnnualDues, fees.AnnualDues);
        Assert.Equal(1700m, fees.RegistrationTotal);
    }

    [Fact]
    public async Task UpdateFeesAsync_PersistsAndIsReadBack()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);

        Assert.True((await service.UpdateFeesAsync(new UpdateMembershipFeesRequest(2000m, 250m, 750m))).Succeeded);
        var fees = await service.GetFeesAsync();

        Assert.Equal(2000m, fees.MembershipFee);
        Assert.Equal(250m, fees.ShippingFee);
        Assert.Equal(750m, fees.AnnualDues);
        Assert.Equal(2250m, fees.RegistrationTotal);
    }

    [Fact]
    public async Task UpdateFeesAsync_WithANegativeFee_IsRefused()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);

        var result = await service.UpdateFeesAsync(new UpdateMembershipFeesRequest(-1m, 200m, 600m));

        Assert.False(result.Succeeded);
    }

    private static async Task<(Member Member, EventRegistration Registration)> SeedEventRegistrationAsync(
        TestDbContext db, EventRegistrationStatus status = EventRegistrationStatus.Registered)
    {
        var user = new ApplicationUser { UserName = $"{Guid.NewGuid()}@example.com", Email = $"{Guid.NewGuid()}@example.com" };
        db.Add(user);
        var member = new Member { UserId = user.Id, User = user, FirstName = "Ana", LastName = "Reyes", Chapter = Chapters.Ncr, MemberType = MemberTypes.Regular };
        db.Members.Add(member);

        var @event = new Event { Title = "Seminar", StartsAt = DateTimeOffset.UtcNow.AddDays(5), EndsAt = DateTimeOffset.UtcNow.AddDays(5).AddHours(4), Fee = 500m };
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
}
