using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Caching;
using PSMPE.Portal.Application.Common.Configuration;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Payments.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Payments;

/// <summary>
/// Membership payments - the initial fee and annual renewals. Verifying a payment here is the only
/// thing in the product that sets MembershipStatus.Active or moves RenewalDueDate; both used to be
/// manual admin edits. See openspecs/payments.md.
/// </summary>
public class PaymentService(IApplicationDbContext db, ICacheService? cache = null) : IPaymentService
{
    // Optional so tests can construct the service directly and get no-op caching, matching
    // MemberService/ContentService.
    private ICacheService Cache => cache ?? NoOpCacheService.Instance;

    private static PaymentDto ToDto(Payment p) => new(
        p.Id, p.MemberId,
        $"{p.Member.FirstName} {p.Member.LastName}".Trim(),
        p.Member.MembershipNo,
        p.Kind, p.Amount, p.IncludesPortalAccess, p.ReferenceNo, p.PaidOn,
        p.ProofStorageKey is not null,
        p.Status, p.RejectedReason, p.DecidedAt, p.CoversUntil, p.CreatedAt,
        p.EventRegistration?.Event.Title, p.EventRegistrationId);

    public async Task<PagedResult<PaymentDto>> GetAllAsync(
        int page, int pageSize, PaymentStatus? status = null, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Payments.AsNoTracking().Include(p => p.Member)
            .Include(p => p.EventRegistration).ThenInclude(er => er!.Event)
            .AsQueryable();
        if (status is not null)
        {
            query = query.Where(p => p.Status == status);
        }

        // Oldest first - a payment queue is worked front to back, and someone who paid three weeks
        // ago should not sit behind this morning's submissions.
        query = query.OrderBy(p => p.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<PaymentDto>(items.Select(ToDto).ToList(), totalCount, page, pageSize);
    }

    public async Task<IReadOnlyList<PaymentDto>> GetForMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var items = await db.Payments.AsNoTracking().Include(p => p.Member)
            .Include(p => p.EventRegistration).ThenInclude(er => er!.Event)
            .Where(p => p.MemberId == memberId)
            // Newest first here - a member reads their own history most-recent-first.
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
        return items.Select(ToDto).ToList();
    }

    public async Task<PaymentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var payment = await db.Payments.AsNoTracking().Include(p => p.Member)
            .Include(p => p.EventRegistration).ThenInclude(er => er!.Event)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return payment is null ? null : ToDto(payment);
    }

    public Task<string?> GetProofKeyAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Payments.AsNoTracking().Where(p => p.Id == id).Select(p => p.ProofStorageKey).FirstOrDefaultAsync(cancellationToken);

    public async Task<Result<PaymentDto>> SubmitAsync(
        Guid userId, SubmitPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.Include(m => m.User).FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return Result<PaymentDto>.NotFound("You don't have a membership profile yet.");
        }

        if (request.Amount <= 0)
        {
            return Result<PaymentDto>.Failure("Amount must be greater than zero.");
        }

        if (request.PaidOn > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return Result<PaymentDto>.Failure("Payment date can't be in the future.");
        }

        if (request.ReferenceNo?.Length > 64)
        {
            return Result<PaymentDto>.Failure("Reference number must be 64 characters or fewer.");
        }

        // One decision at a time. Without this a member could queue several submissions and an
        // admin verifying two of them would advance the due date twice for one year's dues.
        var hasPending = await db.Payments.AnyAsync(
            p => p.MemberId == member.Id && p.Status == PaymentStatus.Submitted, cancellationToken);
        if (hasPending)
        {
            return Result<PaymentDto>.Conflict("You already have a payment awaiting verification.");
        }

        // Derived, not taken from the caller: a member can't claim a renewal for a membership that
        // was never activated, nor a second "new membership" payment once they're active.
        var kind = member.RenewalDueDate is null ? PaymentKind.NewMembership : PaymentKind.Renewal;

        // Captured independently of the caller-declared Amount, same as GetFeesAsync resolves the
        // other three fees - this is "what PortalFee was configured (net of any promotion) when
        // this payment was made," so later fee/promo edits can never retroactively change what a
        // historical payment's portal-revenue contribution was. Zero when the add-on isn't included.
        var portalFeeAmount = request.IncludePortalAccess
            ? await FeePromotionResolver.ResolveCurrentAsync(db, MembershipFeeKeys.PortalFee, MembershipFeeKeys.DefaultPortalFee, cancellationToken)
            : 0m;

        var payment = new Payment
        {
            MemberId = member.Id,
            Member = member,
            Kind = kind,
            Amount = request.Amount,
            ReferenceNo = request.ReferenceNo?.Trim(),
            PaidOn = request.PaidOn,
            Status = PaymentStatus.Submitted,
            // Whatever the member declared - no server-side forcing or branching, and no
            // consistency check against Amount (mismatch guarding is a UI safety net only).
            IncludesPortalAccess = request.IncludePortalAccess,
            PortalFeeAmount = portalFeeAmount,
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);
        return Result<PaymentDto>.Success(ToDto(payment));
    }

    public async Task<Result> AttachProofAsync(Guid paymentId, string storageKey, CancellationToken cancellationToken = default)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null)
        {
            return Result.NotFound($"Payment '{paymentId}' was not found.");
        }

        if (payment.Status != PaymentStatus.Submitted)
        {
            return Result.Failure("This payment has already been decided - its proof can no longer be changed.");
        }

        payment.ProofStorageKey = storageKey;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> VerifyAsync(Guid paymentId, Guid decidedByUserId, CancellationToken cancellationToken = default)
    {
        var payment = await db.Payments.Include(p => p.Member)
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null)
        {
            return Result.NotFound($"Payment '{paymentId}' was not found.");
        }

        if (payment.Status == PaymentStatus.Verified)
        {
            // Idempotent, same as ApproveAsync - a repeat call must not advance the due date again.
            return Result.Success();
        }

        if (payment.Status == PaymentStatus.Rejected)
        {
            return Result.Failure("This payment was rejected. The member needs to submit a new one.");
        }

        if (payment.ProofStorageKey is null)
        {
            return Result.Failure("This payment has no proof attached - there's nothing to verify against.");
        }

        if (payment.Kind == PaymentKind.EventRegistration)
        {
            var registration = payment.EventRegistrationId is null
                ? null
                : await db.EventRegistrations.FirstOrDefaultAsync(r => r.Id == payment.EventRegistrationId, cancellationToken);
            if (registration is null)
            {
                return Result.Failure("The event registration for this payment no longer exists.");
            }
            if (registration.Status != EventRegistrationStatus.PaymentSubmitted)
            {
                return Result.Failure("This registration isn't awaiting payment verification.");
            }

            EventPaymentVerification.Apply(payment, registration, decidedByUserId);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        var member = payment.Member;

        // Payment can't admit someone. Approval is a separate decision that gates on RMP
        // verification (see MemberService.ApproveAsync); paying doesn't bypass it.
        if (member.ApprovedAt is null)
        {
            return Result.Failure("This member's application hasn't been approved yet, so their payment can't activate a membership.");
        }

        PaymentVerification.Apply(payment, member, decidedByUserId);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> RejectAsync(Guid paymentId, string reason, Guid decidedByUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure("A reason is required to reject a payment.");
        }

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null)
        {
            return Result.NotFound($"Payment '{paymentId}' was not found.");
        }

        if (payment.Status == PaymentStatus.Verified)
        {
            // Reversing a verification would have to un-advance a due date (or un-attend a
            // registration) - deliberately not a thing this endpoint does.
            return Result.Failure("This payment was already verified and can't be rejected.");
        }

        if (payment.Kind == PaymentKind.EventRegistration && payment.EventRegistrationId is not null)
        {
            var registration = await db.EventRegistrations.FirstOrDefaultAsync(r => r.Id == payment.EventRegistrationId, cancellationToken);
            if (registration is not null)
            {
                registration.Status = EventRegistrationStatus.Rejected;
                registration.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        payment.Status = PaymentStatus.Rejected;
        payment.RejectedReason = reason.Trim();
        payment.DecidedByUserId = decidedByUserId;
        payment.DecidedAt = DateTimeOffset.UtcNow;
        payment.UpdatedAt = DateTimeOffset.UtcNow;

        // Member Status and RenewalDueDate are deliberately untouched for a NewMembership/Renewal
        // rejection - a rejected renewal leaves the member exactly where they were, still owing.
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<PaymentDto>> SubmitForEventRegistrationAsync(
        Guid userId, Guid registrationId, SubmitPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var registration = await db.EventRegistrations.Include(r => r.Member).Include(r => r.Event)
            .FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);
        if (registration is null)
        {
            return Result<PaymentDto>.NotFound($"Registration '{registrationId}' was not found.");
        }
        if (registration.Member.UserId != userId)
        {
            return Result<PaymentDto>.Forbidden("This isn't your registration.");
        }
        // PaymentSubmitted is deliberately not excluded here - that status only ever coexists with
        // an actual Submitted Payment row (set together in this method and in
        // RecordEventCashPaymentAsync), so the hasPending check below is what turns a second
        // submission while one is already pending into a Conflict rather than this Validation.
        if (registration.Status is EventRegistrationStatus.PaymentVerified or EventRegistrationStatus.Attended
            or EventRegistrationStatus.EvaluationSubmitted or EventRegistrationStatus.Cancelled)
        {
            return Result<PaymentDto>.Failure("This registration isn't awaiting payment.");
        }

        if (request.Amount <= 0)
        {
            return Result<PaymentDto>.Failure("Amount must be greater than zero.");
        }
        if (request.PaidOn > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return Result<PaymentDto>.Failure("Payment date can't be in the future.");
        }
        if (request.ReferenceNo?.Length > 64)
        {
            return Result<PaymentDto>.Failure("Reference number must be 64 characters or fewer.");
        }

        var hasPending = await db.Payments.AnyAsync(
            p => p.EventRegistrationId == registrationId && p.Status == PaymentStatus.Submitted, cancellationToken);
        if (hasPending)
        {
            return Result<PaymentDto>.Conflict("You already have a payment awaiting verification for this registration.");
        }

        var payment = new Payment
        {
            MemberId = registration.MemberId,
            Member = registration.Member,
            Kind = PaymentKind.EventRegistration,
            EventRegistrationId = registration.Id,
            EventRegistration = registration,
            Amount = request.Amount,
            ReferenceNo = request.ReferenceNo?.Trim(),
            PaidOn = request.PaidOn,
            Status = PaymentStatus.Submitted,
        };
        db.Payments.Add(payment);

        registration.Status = EventRegistrationStatus.PaymentSubmitted;
        registration.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Result<PaymentDto>.Success(ToDto(payment));
    }

    public async Task<Result<PaymentDto>> RecordEventCashPaymentAsync(
        Guid registrationId, decimal amount, Guid decidedByUserId, CancellationToken cancellationToken = default)
    {
        var registration = await db.EventRegistrations.Include(r => r.Member).Include(r => r.Event)
            .FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);
        if (registration is null)
        {
            return Result<PaymentDto>.NotFound($"Registration '{registrationId}' was not found.");
        }

        // Same terminal/already-decided statuses SubmitForEventRegistrationAsync refuses, plus
        // Cancelled: CancelRegistrationAsync allows Registered/PaymentSubmitted/Rejected ->
        // Cancelled and never touches the Payment row, so a registration cancelled before ever
        // paying (or cancelled after a rejection) has no active Payment - hasActivePayment below
        // would be false and let this method resurrect a cancelled registration. PaymentSubmitted
        // is deliberately not listed here: cancelling from it leaves the Submitted Payment row in
        // place, so hasActivePayment already turns that case into a Conflict.
        if (registration.Status is EventRegistrationStatus.PaymentVerified or EventRegistrationStatus.Attended
            or EventRegistrationStatus.EvaluationSubmitted or EventRegistrationStatus.Cancelled)
        {
            return Result<PaymentDto>.Failure("This registration isn't awaiting payment.");
        }

        if (amount <= 0)
        {
            return Result<PaymentDto>.Failure("Amount must be greater than zero.");
        }

        // "Exactly one Payment, regardless of path" - a Rejected payment doesn't count, same as
        // SubmitForEventRegistrationAsync's own pending check, so a cash payment can still cover a
        // registration whose earlier proof submission was rejected.
        var hasActivePayment = await db.Payments.AnyAsync(
            p => p.EventRegistrationId == registrationId && p.Status != PaymentStatus.Rejected, cancellationToken);
        if (hasActivePayment)
        {
            return Result<PaymentDto>.Conflict("This registration already has a submitted or verified payment.");
        }

        var payment = new Payment
        {
            MemberId = registration.MemberId,
            Member = registration.Member,
            Kind = PaymentKind.EventRegistration,
            EventRegistrationId = registration.Id,
            EventRegistration = registration,
            Amount = amount,
            PaidOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = PaymentStatus.Submitted,
        };
        db.Payments.Add(payment);

        EventPaymentVerification.Apply(payment, registration, decidedByUserId);

        await db.SaveChangesAsync(cancellationToken);
        return Result<PaymentDto>.Success(ToDto(payment));
    }

    public Task<MembershipFeesDto> GetFeesAsync(CancellationToken cancellationToken = default) =>
        Cache.GetOrCreateAsync(MembershipFeeKeys.CacheKey, "Cache:MembershipFeesDurationSeconds", 600, async () =>
        {
            var keys = MembershipFeeKeys.All.Select(f => f.Key).ToArray();
            var rows = await db.SystemConfigs.AsNoTracking()
                .Where(c => keys.Contains(c.Key))
                .ToDictionaryAsync(c => c.Key, c => c.Value, cancellationToken);

            // Resolved through FeePromotionResolver, not the plain configured value, so an active
            // promotion is reflected here without a separate code path - still inside this single
            // cached factory, so a promotion starting/ending exactly at a date boundary can be up to
            // 10 minutes late to reflect, same accepted tradeoff as a manual fee edit today.
            var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
            var membershipFee = await FeePromotionResolver.ResolveAsync(
                db, MembershipFeeKeys.MembershipFee,
                Read(rows, MembershipFeeKeys.MembershipFee, MembershipFeeKeys.DefaultMembershipFee), asOf, cancellationToken);
            var shippingFee = await FeePromotionResolver.ResolveAsync(
                db, MembershipFeeKeys.ShippingFee,
                Read(rows, MembershipFeeKeys.ShippingFee, MembershipFeeKeys.DefaultShippingFee), asOf, cancellationToken);
            var annualDues = await FeePromotionResolver.ResolveAsync(
                db, MembershipFeeKeys.AnnualDues,
                Read(rows, MembershipFeeKeys.AnnualDues, MembershipFeeKeys.DefaultAnnualDues), asOf, cancellationToken);
            var portalFee = await FeePromotionResolver.ResolveAsync(
                db, MembershipFeeKeys.PortalFee,
                Read(rows, MembershipFeeKeys.PortalFee, MembershipFeeKeys.DefaultPortalFee), asOf, cancellationToken);

            return new MembershipFeesDto(membershipFee, shippingFee, annualDues, portalFee);
        });

    /// <summary>Falls back to the shipped constant for a missing or unparseable row, so a bad config
    /// value shows the old price rather than charging zero.</summary>
    private static decimal Read(IReadOnlyDictionary<string, string> rows, string key, decimal fallback) =>
        rows.TryGetValue(key, out var raw) && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    public async Task<Result> UpdateFeesAsync(UpdateMembershipFeesRequest request, CancellationToken cancellationToken = default)
    {
        var values = new (string Key, decimal Value)[]
        {
            (MembershipFeeKeys.MembershipFee, request.MembershipFee),
            (MembershipFeeKeys.ShippingFee, request.ShippingFee),
            (MembershipFeeKeys.AnnualDues, request.AnnualDues),
            (MembershipFeeKeys.PortalFee, request.PortalFee),
        };

        if (values.Any(v => v.Value < 0))
        {
            return Result.Failure("Fees can't be negative.");
        }

        foreach (var (key, value) in values)
        {
            var row = await db.SystemConfigs.FirstOrDefaultAsync(c => c.Key == key, cancellationToken);
            if (row is null)
            {
                var description = MembershipFeeKeys.All.First(f => f.Key == key).Description;
                db.SystemConfigs.Add(new SystemConfig { Key = key, Value = value.ToString(CultureInfo.InvariantCulture), Description = description });
            }
            else
            {
                row.Value = value.ToString(CultureInfo.InvariantCulture);
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // This is the first write path to SystemConfigs in the product - every other consumer
        // assumed the table was seed-only and TTL expiry was enough. Evicting here is what keeps
        // the wizard's total and the receipt from showing a stale price for up to 10 minutes.
        Cache.Remove(MembershipFeeKeys.CacheKey);
        return Result.Success();
    }

    private static FeePromotionDto ToPromotionDto(FeePromotion p) =>
        new(p.Id, p.FeeKey, p.PromoAmount, p.StartDate, p.EndDate, p.CreatedByUserId, p.CreatedAt);

    public async Task<IReadOnlyList<FeePromotionDto>> GetPromotionsAsync(CancellationToken cancellationToken = default)
    {
        var promotions = await db.FeePromotions.AsNoTracking()
            // Newest-starting first - this is an admin configuration list, not a fee read, so the
            // most recently scheduled promotion is what someone editing this screen cares about.
            .OrderByDescending(p => p.StartDate)
            .ToListAsync(cancellationToken);
        return promotions.Select(ToPromotionDto).ToList();
    }

    public async Task<Result<FeePromotionDto>> CreatePromotionAsync(
        CreateFeePromotionRequest request, Guid createdByUserId, CancellationToken cancellationToken = default)
    {
        if (!MembershipFeeKeys.All.Any(f => f.Key == request.FeeKey))
        {
            return Result<FeePromotionDto>.Failure($"'{request.FeeKey}' is not a recognized fee.");
        }

        if (request.StartDate > request.EndDate)
        {
            return Result<FeePromotionDto>.Failure("Start date must be on or before the end date.");
        }

        if (request.PromoAmount < 0)
        {
            return Result<FeePromotionDto>.Failure("Promo amount can't be negative.");
        }

        // Inclusive overlap check: two ranges overlap unless one ends before the other starts. Kept
        // to one row active per FeeKey per day so FeePromotionResolver never has to pick among
        // several matches.
        var overlaps = await db.FeePromotions.AsNoTracking().AnyAsync(
            p => p.FeeKey == request.FeeKey && p.StartDate <= request.EndDate && p.EndDate >= request.StartDate,
            cancellationToken);
        if (overlaps)
        {
            return Result<FeePromotionDto>.Conflict("A promotion for this fee already covers part of that date range.");
        }

        var promotion = new FeePromotion
        {
            FeeKey = request.FeeKey,
            PromoAmount = request.PromoAmount,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CreatedByUserId = createdByUserId,
        };
        db.FeePromotions.Add(promotion);
        await db.SaveChangesAsync(cancellationToken);

        // A promotion covering today changes what GetFeesAsync's cached factory should return,
        // exactly like UpdateFeesAsync's own edit - evicted here for the same reason.
        Cache.Remove(MembershipFeeKeys.CacheKey);

        return Result<FeePromotionDto>.Success(ToPromotionDto(promotion));
    }

    public async Task<Result> DeletePromotionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var promotion = await db.FeePromotions.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (promotion is null)
        {
            return Result.NotFound($"Promotion '{id}' was not found.");
        }

        // Hard delete: this is a lightweight promotional record, not an audited financial
        // transaction like Payment - nothing downstream references a FeePromotion by Id once it's
        // gone, since already-created Payments captured their own amount at submission time.
        db.FeePromotions.Remove(promotion);
        await db.SaveChangesAsync(cancellationToken);
        Cache.Remove(MembershipFeeKeys.CacheKey);
        return Result.Success();
    }

    public async Task<Result<PaymentReportSummaryDto>> GetReportSummaryAsync(
        DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        if (startDate > endDate)
        {
            return Result<PaymentReportSummaryDto>.Failure("Start date must be on or before the end date.");
        }

        // Verified only - a Submitted or Rejected payment isn't real revenue yet. NewMembership/
        // Renewal only - EventRegistration is a separate revenue stream (see proposal.md). PaidOn
        // range is inclusive on both ends, matching FeePromotion's own StartDate/EndDate convention.
        var query = db.Payments.AsNoTracking().Where(p =>
            p.Status == PaymentStatus.Verified &&
            (p.Kind == PaymentKind.NewMembership || p.Kind == PaymentKind.Renewal) &&
            p.PaidOn >= startDate && p.PaidOn <= endDate);

        var membershipOnly = query.Where(p => !p.IncludesPortalAccess);
        var combined = query.Where(p => p.IncludesPortalAccess);

        var membershipOnlyCount = await membershipOnly.CountAsync(cancellationToken);
        var membershipOnlyTotal = await membershipOnly.SumAsync(p => p.Amount, cancellationToken);
        var combinedCount = await combined.CountAsync(cancellationToken);
        var combinedTotal = await combined.SumAsync(p => p.Amount, cancellationToken);

        // Filtered explicitly to the combined subset rather than relying on the (true today, but
        // not worth depending on silently) invariant that PortalFeeAmount is always zero on a
        // membership-only payment - see PaymentService.SubmitAsync and
        // MemberService.EnsureRegistrationPaymentAsync/ResolveRegistrationPaymentAsync, the three
        // call sites that stamp it.
        var portalRevenueTotal = await combined.SumAsync(p => p.PortalFeeAmount, cancellationToken);

        // SumAsync over zero matching rows returns 0m, not null/an exception - guaranteed by LINQ
        // for a non-nullable numeric selector, and EF Core's SQL translation wraps SUM() in
        // COALESCE(..., 0) for the same reason, so this holds against both the InMemory provider
        // used by this project's unit tests and the real Npgsql provider in production.
        return Result<PaymentReportSummaryDto>.Success(new PaymentReportSummaryDto(
            membershipOnlyCount, membershipOnlyTotal, combinedCount, combinedTotal, portalRevenueTotal));
    }
}
