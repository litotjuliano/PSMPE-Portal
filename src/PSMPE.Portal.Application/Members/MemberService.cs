using System.Linq.Expressions;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Caching;
using PSMPE.Portal.Application.Common.Configuration;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Members.Dtos;
using PSMPE.Portal.Application.Payments;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Members;

public class MemberService(IApplicationDbContext db, ICacheService? cache = null) : IMemberService
{
    // Optional so the 43 existing unit tests that construct MemberService directly
    // (new MemberService(db)) keep compiling unchanged and get no-op caching - see
    // ContentService for the same pattern/rationale, and docs/caching-strategy.md.
    private ICacheService Cache => cache ?? NoOpCacheService.Instance;

    private const int RenewalDueSoonDays = 60;

    /// <summary>
    /// Shared by GetAllAsync's pendingPrcVerificationOnly filter and GetStatsAsync's action-item
    /// count, so the two can never silently desync - a proposed change to an already-verified value
    /// (PendingPrcLicenseNo set), or a PRC No. that has never been reviewed at all (submitted at
    /// registration, never since verified or changed), both need an admin decision.
    /// </summary>
    private static readonly Expression<Func<Member, bool>> PendingPrcVerificationPredicate =
        m => m.PendingPrcLicenseNo != null
            || (!m.PrcIdVerified && m.PrcLicenseNo != null && m.PendingPrcLicenseNo == null);

    /// <summary>
    /// A member keeps limited portal access for GracePeriodDays after RenewalDueDate lapses,
    /// rather than losing access the instant it passes.
    ///
    /// Computed purely from RenewalDueDate rather than gated on Status == Active, so this stays
    /// correct both before and after MembershipLifecycleService's daily job flips a lapsed member's
    /// Status to Expired - Status is no longer a reliable signal of "hasn't lapsed yet" once that
    /// job exists. Deactivated is excluded explicitly: it's a distinct admin action (not a
    /// lapsed-payment state), so a Deactivated member with a stale due date must not show the same
    /// "renew now" messaging a normal lapsed member gets.
    /// </summary>
    private static bool ComputeIsInGracePeriod(Member m, int gracePeriodDays)
    {
        if (m.Status == MembershipStatus.Deactivated || m.RenewalDueDate is null)
        {
            return false;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (m.RenewalDueDate.Value >= today)
        {
            return false;
        }

        return today <= m.RenewalDueDate.Value.AddDays(gracePeriodDays);
    }

    /// <summary>
    /// Past the renewal date *and* past the grace period. Still derived rather than trusted from
    /// Status alone - MembershipLifecycleService's daily tick keeps persisted Status in sync, but
    /// this DTO flag stays same-day-accurate for the hours between ticks. See
    /// ComputeIsInGracePeriod's doc comment for why Status == Active is no longer a guard here, and
    /// why Deactivated is still excluded.
    /// </summary>
    private static bool ComputeIsExpired(Member m, int gracePeriodDays)
    {
        if (m.Status == MembershipStatus.Deactivated || m.RenewalDueDate is null)
        {
            return false;
        }

        return DateOnly.FromDateTime(DateTime.UtcNow) > m.RenewalDueDate.Value.AddDays(gracePeriodDays);
    }

    private static MemberDto ToDto(Member m, int gracePeriodDays) => new(
        m.Id, m.UserId, m.User.Email ?? string.Empty, m.FirstName, m.MiddleName, m.LastName, m.Suffix,
        m.Birthdate, m.Gender, m.CivilStatus,
        m.EducationLevel, m.SchoolName, m.CourseYearGraduated, m.SpecifiedProfession,
        m.MobileNumber,
        m.HouseNo, m.Street, m.Barangay, m.CityMunicipality, m.Province, m.ZipCode, m.Country,
        m.MailingHouseNo, m.MailingStreet, m.MailingBarangay, m.MailingCityMunicipality, m.MailingProvince, m.MailingZipCode, m.MailingCountry,
        m.HousePhone,
        m.MembershipNo, m.PrcLicenseNo, m.PrcRegistrationDate, m.PrcValidUntilDate,
        m.PtrNumber, m.PtrPlaceIssued, m.PtrDateIssued, m.Tin, m.PrcIdVerified,
        m.PendingPrcLicenseNo, m.PendingPrcRegistrationDate, m.PendingPrcValidUntilDate, m.PrcVerificationRejectedReason,
        m.Chapter, m.ChapterYear, m.ChapterPosition,
        m.EmploymentStatus, m.Company, m.Position, m.BusinessAddress, m.YearsOfPractice, m.Specialization, m.Skills,
        m.MemberType,
        m.Status, m.RenewalDueDate, m.HasPortalAccess, m.NationalDuesReferenceNo, m.ApprovedAt, m.SubmittedAt,
        ComputeIsInGracePeriod(m, gracePeriodDays), ComputeIsExpired(m, gracePeriodDays), m.CreatedAt, m.UpdatedAt);

    public async Task<PagedResult<MemberDto>> GetAllAsync(
        int page, int pageSize, string sortBy, string sortDir, MembershipStatus? status,
        bool? pendingApprovalOnly = null, bool? pendingPrcVerificationOnly = null, string? search = null,
        IReadOnlyCollection<Guid>? excludeUserIds = null, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Drafts (SubmittedAt == null, still mid-wizard via the per-step autosave) are invisible
        // to admins entirely - a half-filled application isn't a "member" yet.
        IQueryable<Member> query = db.Members.AsNoTracking().Include(m => m.User).Where(m => m.SubmittedAt != null);

        if (excludeUserIds is { Count: > 0 })
        {
            // Administrative/staff accounts (Super Admin, Admin, Manager, Accounts) never belong
            // in Member-facing lists - excluded here, before CountAsync, so the total count and
            // pagination are correct too, not just the visible page.
            query = query.Where(m => !excludeUserIds.Contains(m.UserId));
        }

        if (status is not null)
        {
            query = query.Where(m => m.Status == status);
        }

        if (pendingApprovalOnly == true)
        {
            // Distinct from Status: an application can be approved but still Status.Pending
            // (approved-but-unpaid, per the annual-dues business rule) - this filter is
            // specifically "has an admin reviewed this yet", used by the Membership Approvals
            // queue and the notification bell so approved items actually disappear from view.
            query = query.Where(m => m.ApprovedAt == null);
        }

        if (pendingPrcVerificationOnly == true)
        {
            query = query.Where(PendingPrcVerificationPredicate);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Same case-insensitive .ToLower().Contains() idiom already used for MembershipNo
            // comparisons elsewhere in this file (see MembershipNoExistsAsync).
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(m =>
                m.FirstName.ToLower().Contains(normalizedSearch)
                || m.LastName.ToLower().Contains(normalizedSearch)
                || (m.MembershipNo != null && m.MembershipNo.ToLower().Contains(normalizedSearch))
                || (m.User.Email != null && m.User.Email.ToLower().Contains(normalizedSearch)));
        }

        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = sortBy.ToLowerInvariant() switch
        {
            "membershipno" => descending ? query.OrderByDescending(m => m.MembershipNo) : query.OrderBy(m => m.MembershipNo),
            "chapter" => descending ? query.OrderByDescending(m => m.Chapter) : query.OrderBy(m => m.Chapter),
            "status" => descending ? query.OrderByDescending(m => m.Status) : query.OrderBy(m => m.Status),
            // Oldest application first is the natural order for the approval queue - it has always
            // shown an "Applied" column but had no way to sort by it.
            "submittedat" => descending ? query.OrderByDescending(m => m.SubmittedAt) : query.OrderBy(m => m.SubmittedAt),
            _ => descending ? query.OrderByDescending(m => m.LastName) : query.OrderBy(m => m.LastName),
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var gracePeriodDays = await MembershipGracePeriod.GetDaysAsync(db, Cache, cancellationToken);
        return new PagedResult<MemberDto>(items.Select(m => ToDto(m, gracePeriodDays)).ToList(), totalCount, page, pageSize);
    }

    /// <summary>
    /// Aggregated counts/trends for the admin dashboard. Not cached (unlike MembershipGracePeriod.GetDaysAsync
    /// above) - this is one dashboard load, not a hot path.
    /// </summary>
    public async Task<MemberStatsDto> GetStatsAsync(IReadOnlyCollection<Guid>? excludeUserIds, CancellationToken cancellationToken = default)
    {
        // Same base filter as GetAllAsync: drafts are invisible, and administrative/staff accounts
        // never belong in Member-facing counts.
        IQueryable<Member> baseQuery = db.Members.AsNoTracking().Where(m => m.SubmittedAt != null);
        if (excludeUserIds is { Count: > 0 })
        {
            baseQuery = baseQuery.Where(m => !excludeUserIds.Contains(m.UserId));
        }

        var statusGroups = await baseQuery
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var statusCounts = statusGroups.ToDictionary(g => g.Status, g => g.Count);
        var statusCountsDto = new MemberStatusCountsDto(
            statusCounts.GetValueOrDefault(MembershipStatus.Pending),
            statusCounts.GetValueOrDefault(MembershipStatus.Active),
            statusCounts.GetValueOrDefault(MembershipStatus.Expired),
            statusCounts.GetValueOrDefault(MembershipStatus.Deactivated));

        // Dataset is tiny, so the 12-month zero-fill happens in code rather than generating a SQL
        // calendar - the grouping itself still runs in the database.
        var monthlyGroups = await baseQuery
            .GroupBy(m => new { m.SubmittedAt!.Value.Year, m.SubmittedAt.Value.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var monthlyLookup = monthlyGroups.ToDictionary(g => (g.Year, g.Month), g => g.Count);
        var currentMonth = new DateTime(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1);
        var registrationTrend = new List<MonthlyRegistrationCountDto>(12);
        for (var i = 11; i >= 0; i--)
        {
            var month = currentMonth.AddMonths(-i);
            registrationTrend.Add(new MonthlyRegistrationCountDto(
                month.Year, month.Month, monthlyLookup.GetValueOrDefault((month.Year, month.Month))));
        }

        var chapterGroups = await baseQuery
            .GroupBy(m => m.Chapter)
            .Select(g => new { Chapter = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var chapterLookup = chapterGroups.ToDictionary(g => g.Chapter, g => g.Count);
        var byChapter = Chapters.All.Select(c => new NamedCountDto(c, chapterLookup.GetValueOrDefault(c))).ToList();

        var typeGroups = await baseQuery
            .GroupBy(m => m.MemberType)
            .Select(g => new { MemberType = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var typeLookup = typeGroups.ToDictionary(g => g.MemberType, g => g.Count);
        var byMemberType = MemberTypes.All.Select(t => new NamedCountDto(t, typeLookup.GetValueOrDefault(t))).ToList();

        var pendingApprovals = await baseQuery.CountAsync(m => m.ApprovedAt == null, cancellationToken);
        var pendingPrcVerification = await baseQuery.CountAsync(PendingPrcVerificationPredicate, cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dueSoonThreshold = today.AddDays(RenewalDueSoonDays);
        var renewalsDueSoon = await baseQuery.CountAsync(
            m => m.Status == MembershipStatus.Active
                && m.RenewalDueDate != null
                && m.RenewalDueDate >= today
                && m.RenewalDueDate <= dueSoonThreshold,
            cancellationToken);

        var actionItems = new MemberActionItemsDto(pendingApprovals, pendingPrcVerification, renewalsDueSoon);

        return new MemberStatsDto(statusCountsDto, registrationTrend, byChapter, byMemberType, actionItems);
    }

    public async Task<MemberDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.AsNoTracking().Include(m => m.User).FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (member is null)
        {
            return null;
        }

        var gracePeriodDays = await MembershipGracePeriod.GetDaysAsync(db, Cache, cancellationToken);
        return ToDto(member, gracePeriodDays);
    }

    public async Task<MemberDto?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.AsNoTracking().Include(m => m.User).FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return null;
        }

        var gracePeriodDays = await MembershipGracePeriod.GetDaysAsync(db, Cache, cancellationToken);
        return ToDto(member, gracePeriodDays);
    }

    /// <summary><paramref name="excludeMemberId"/> lets an edit re-save a member's own number
    /// without colliding with itself.</summary>
    /// <summary>
    /// Case-insensitive on purpose: "PSMPE-001" and "psmpe-001" are the same control number to
    /// everyone except a byte comparison, and letting both through would put two members on one
    /// number. Matched by the case-insensitive unique index the same comparison relies on - see
    /// the MembershipNoCaseInsensitiveUnique migration - so a concurrent insert can't slip past
    /// this check either.
    /// </summary>
    public Task<bool> MembershipNoExistsAsync(string membershipNo, Guid? excludeMemberId = null, CancellationToken cancellationToken = default)
    {
        var normalized = membershipNo.Trim().ToLowerInvariant();
        return db.Members.AsNoTracking()
            .AnyAsync(
                m => m.MembershipNo != null
                    && m.MembershipNo.ToLower() == normalized
                    && (excludeMemberId == null || m.Id != excludeMemberId),
                cancellationToken);
    }

    public async Task<Result<MemberDto>> CreateAsync(CreateMemberRequest request, CancellationToken cancellationToken = default)
    {
        // Admin-created profiles skip the wizard and are marked submitted immediately, so nothing
        // else would ever demand a licence number from them - but without one a member can never
        // enter the verification queue (see GetAllAsync's pendingPrcVerificationOnly filter), and
        // therefore can never be approved now that approval requires verification. Requiring it
        // here matches what SubmitMyProfileAsync already demands of self-service applicants.
        if (string.IsNullOrWhiteSpace(request.PrcLicenseNo))
        {
            return Result<MemberDto>.Failure("RMP License No. is required - a member without one can't be verified or approved.");
        }

        var lengthError = ValidateMemberFieldLengths(
            request.FirstName, request.MiddleName, request.LastName, request.Suffix,
            request.CivilStatus, request.Chapter, request.MemberType,
            request.EducationLevel, request.SchoolName, request.CourseYearGraduated, request.SpecifiedProfession,
            request.HouseNo, request.Street, request.Barangay, request.CityMunicipality, request.Province, request.ZipCode, request.Country,
            request.MailingHouseNo, request.MailingStreet, request.MailingBarangay, request.MailingCityMunicipality, request.MailingProvince, request.MailingZipCode, request.MailingCountry,
            request.PrcLicenseNo, request.PtrNumber, request.PtrPlaceIssued, request.Company, request.Position,
            request.ChapterPosition,
            request.BusinessAddress, request.Specialization, request.Skills, request.EmploymentStatus);
        if (lengthError is not null)
        {
            return Result<MemberDto>.Failure(lengthError);
        }

        var member = new Member
        {
            UserId = request.UserId,
            MembershipNo = request.MembershipNo,
            FirstName = request.FirstName,
            MiddleName = request.MiddleName,
            LastName = request.LastName,
            Suffix = request.Suffix,
            Birthdate = request.Birthdate,
            Gender = request.Gender,
            CivilStatus = request.CivilStatus,
            EducationLevel = request.EducationLevel,
            SchoolName = request.SchoolName,
            CourseYearGraduated = request.CourseYearGraduated,
            SpecifiedProfession = request.SpecifiedProfession,
            MobileNumber = request.MobileNumber,
            HouseNo = request.HouseNo,
            Street = request.Street,
            Barangay = request.Barangay,
            CityMunicipality = request.CityMunicipality,
            Province = request.Province,
            ZipCode = request.ZipCode,
            Country = request.Country,
            MailingHouseNo = request.MailingHouseNo,
            MailingStreet = request.MailingStreet,
            MailingBarangay = request.MailingBarangay,
            MailingCityMunicipality = request.MailingCityMunicipality,
            MailingProvince = request.MailingProvince,
            MailingZipCode = request.MailingZipCode,
            MailingCountry = request.MailingCountry,
            HousePhone = request.HousePhone,
            PrcLicenseNo = request.PrcLicenseNo,
            PrcRegistrationDate = request.PrcRegistrationDate,
            PrcValidUntilDate = request.PrcValidUntilDate,
            PtrNumber = request.PtrNumber,
            PtrPlaceIssued = request.PtrPlaceIssued,
            PtrDateIssued = request.PtrDateIssued,
            Tin = request.Tin,
            Chapter = request.Chapter,
            ChapterYear = request.ChapterYear,
            ChapterPosition = request.ChapterPosition,
            EmploymentStatus = request.EmploymentStatus,
            Company = request.Company,
            Position = request.Position,
            BusinessAddress = request.BusinessAddress,
            YearsOfPractice = request.YearsOfPractice,
            Specialization = request.Specialization,
            Skills = request.Skills,
            MemberType = request.MemberType,
            Status = MembershipStatus.Pending,
            // Admin-entered profiles never go through the self-service wizard's draft phase -
            // they're complete the moment an admin creates them, so they must be immediately
            // visible (not hidden as an in-progress draft like a wizard autosave would be).
            SubmittedAt = DateTimeOffset.UtcNow,
            RenewalDueDate = request.RenewalDueDate,
            NationalDuesReferenceNo = request.NationalDuesReferenceNo
        };

        db.Members.Add(member);
        await db.SaveChangesAsync(cancellationToken);
        var dto = await GetByIdAsync(member.Id, cancellationToken) ?? throw new InvalidOperationException("Member was not persisted.");
        return Result<MemberDto>.Success(dto);
    }

    public async Task<Result> UpdateAsync(Guid id, UpdateMemberRequest request, CancellationToken cancellationToken = default)
    {
        var lengthError = ValidateMemberFieldLengths(
            request.FirstName, request.MiddleName, request.LastName, request.Suffix,
            request.CivilStatus, request.Chapter, request.MemberType,
            request.EducationLevel, request.SchoolName, request.CourseYearGraduated, request.SpecifiedProfession,
            request.HouseNo, request.Street, request.Barangay, request.CityMunicipality, request.Province, request.ZipCode, request.Country,
            request.MailingHouseNo, request.MailingStreet, request.MailingBarangay, request.MailingCityMunicipality, request.MailingProvince, request.MailingZipCode, request.MailingCountry,
            request.PrcLicenseNo, request.PtrNumber, request.PtrPlaceIssued, request.Company, request.Position,
            request.ChapterPosition,
            request.BusinessAddress, request.Specialization, request.Skills, request.EmploymentStatus);
        if (lengthError is not null)
        {
            return Result.Failure(lengthError);
        }

        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (member is null)
        {
            return Result.NotFound($"Member '{id}' was not found.");
        }

        // The correction path for a control number mistyped at approval. Without it a typo would be
        // unfixable through the product - the field appears in no other update path, and
        // ApproveAsync is idempotent so re-approving won't overwrite it.
        //
        // Blank means "not supplied", never "clear it": callers that don't know about this field
        // omit it, and treating that as a clear would silently un-number approved members.
        var membershipNo = request.MembershipNo?.Trim();
        if (!string.IsNullOrEmpty(membershipNo))
        {
            if (membershipNo.Length > 32)
            {
                return Result.Failure("Membership ID must be 32 characters or fewer.");
            }

            if (await MembershipNoExistsAsync(membershipNo, id, cancellationToken))
            {
                return Result.Conflict($"Membership ID '{membershipNo}' is already in use.");
            }

            member.MembershipNo = membershipNo;
        }

        member.FirstName = request.FirstName;
        member.MiddleName = request.MiddleName;
        member.LastName = request.LastName;
        member.Suffix = request.Suffix;
        member.Birthdate = request.Birthdate;
        member.Gender = request.Gender;
        member.CivilStatus = request.CivilStatus;
        member.EducationLevel = request.EducationLevel;
        member.SchoolName = request.SchoolName;
        member.CourseYearGraduated = request.CourseYearGraduated;
        member.SpecifiedProfession = request.SpecifiedProfession;
        member.MobileNumber = request.MobileNumber;
        member.HouseNo = request.HouseNo;
        member.Street = request.Street;
        member.Barangay = request.Barangay;
        member.CityMunicipality = request.CityMunicipality;
        member.Province = request.Province;
        member.ZipCode = request.ZipCode;
        member.Country = request.Country;
        member.MailingHouseNo = request.MailingHouseNo;
        member.MailingStreet = request.MailingStreet;
        member.MailingBarangay = request.MailingBarangay;
        member.MailingCityMunicipality = request.MailingCityMunicipality;
        member.MailingProvince = request.MailingProvince;
        member.MailingZipCode = request.MailingZipCode;
        member.MailingCountry = request.MailingCountry;
        member.HousePhone = request.HousePhone;
        member.PrcLicenseNo = request.PrcLicenseNo;
        member.PrcRegistrationDate = request.PrcRegistrationDate;
        member.PrcValidUntilDate = request.PrcValidUntilDate;
        member.PtrNumber = request.PtrNumber;
        member.PtrPlaceIssued = request.PtrPlaceIssued;
        member.PtrDateIssued = request.PtrDateIssued;
        member.Tin = request.Tin;
        member.Chapter = request.Chapter;
        member.ChapterYear = request.ChapterYear;
        member.ChapterPosition = request.ChapterPosition;
        member.EmploymentStatus = request.EmploymentStatus;
        member.Company = request.Company;
        member.Position = request.Position;
        member.BusinessAddress = request.BusinessAddress;
        member.YearsOfPractice = request.YearsOfPractice;
        member.Specialization = request.Specialization;
        member.Skills = request.Skills;
        member.MemberType = request.MemberType;
        member.Status = request.Status;
        member.RenewalDueDate = request.RenewalDueDate;
        member.NationalDuesReferenceNo = request.NationalDuesReferenceNo;
        member.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Assigns PSMPE's membership control number and approves in one step - the number is mandatory
    /// here because nothing else in the product sets it, and the approval email and receipt both
    /// print it.
    ///
    /// Idempotent: approving an already-approved application is a no-op success, and deliberately
    /// does NOT re-assign the number. Without that guard a repeat call - which the controller makes
    /// harmless for the email and receipt - would silently renumber a live member.
    /// </summary>
    /// <summary>
    /// Admits an application: assigns the control number, sets ApprovedAt, and accepts the
    /// registration payment - all in one transaction.
    ///
    /// <para>Approval and payment are deliberately indivisible. Requiring a *verified* payment as a
    /// precondition would deadlock, because verifying a NewMembership payment needs ApprovedAt to
    /// compute the renewal date. Doing both here, in order, means neither outcome is observable
    /// without the other: there is no window in which a member is approved but unpaid.</para>
    /// </summary>
    public async Task<Result> ApproveAsync(
        Guid id, ApproveMemberRequest request, Guid decidedByUserId, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (member is null)
        {
            return Result.NotFound($"Member '{id}' was not found.");
        }

        if (member.ApprovedAt is not null)
        {
            return Result.Success();
        }

        // The RMP licence is the eligibility criterion for membership, so it has to be checked
        // before an application is admitted - approving issues a control number, generates a
        // receipt and emails the member, none of which is cheap to unwind.
        //
        // Deliberately placed *after* the already-approved short-circuit above: members approved
        // before this rule existed keep their approval, and a repeat call on them still succeeds.
        if (!member.PrcIdVerified)
        {
            return Result.Failure("This member's RMP licence must be verified before their application can be approved.");
        }

        var trimmed = request.MembershipNo?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Failure("A Membership ID is required to approve this application.");
        }

        if (trimmed.Length > 32)
        {
            return Result.Failure("Membership ID must be 32 characters or fewer.");
        }

        // The unique index is the real guard against a concurrent duplicate; this check exists so
        // the common case returns a clean conflict instead of a raw DbUpdateException.
        if (await MembershipNoExistsAsync(trimmed, id, cancellationToken))
        {
            return Result.Conflict($"Membership ID '{trimmed}' is already in use.");
        }

        var paymentResult = await ResolveRegistrationPaymentAsync(member, request.Payment, cancellationToken);
        if (!paymentResult.Succeeded)
        {
            return Result.Failure(paymentResult.Error!);
        }

        member.MembershipNo = trimmed;
        member.ApprovedAt = DateTimeOffset.UtcNow;
        member.UpdatedAt = DateTimeOffset.UtcNow;

        // Applied after ApprovedAt is set - the NewMembership due-date arithmetic reads it.
        PaymentVerification.Apply(paymentResult.Value!, member, decidedByUserId);

        // Added to the same SaveChangesAsync call, not a separate IAuditLogService write, so the
        // audit row is atomically all-or-nothing with the approval itself.
        db.AuditLogs.Add(new AuditLog
        {
            EventType = "membership.approved",
            ActorUserId = decidedByUserId,
            TargetType = "Member",
            TargetId = member.Id,
            Metadata = JsonSerializer.Serialize(new { membershipNo = trimmed }),
        });

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Finds the registration payment to accept, creating one first if the admin supplied details
    /// for a member who has none.
    ///
    /// <para>A member with no payment record would otherwise be permanently unapprovable now that
    /// approval requires one - the shape admin-created profiles arrive in, since POST /api/members
    /// never creates a payment. Rather than exempt them (a loophole that would let the admin form
    /// bypass payment entirely), the approving admin records what was actually paid.</para>
    /// </summary>
    private async Task<Result<Payment>> ResolveRegistrationPaymentAsync(
        Member member, RecordPaymentRequest? supplied, CancellationToken cancellationToken)
    {
        var existing = await db.Payments
            .Where(p => p.MemberId == member.Id && p.Kind == PaymentKind.NewMembership)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            if (supplied is not null)
            {
                // Rejected rather than ignored: silently discarding what the admin typed would let
                // them believe they had corrected an amount that in fact never changed.
                return Result<Payment>.Failure(
                    "This member already has a registration payment on record - review it instead of entering a new one.");
            }

            if (existing.Status == PaymentStatus.Rejected)
            {
                return Result<Payment>.Failure(
                    "This member's registration payment was rejected. They need to submit a new one before the application can be approved.");
            }

            if (existing.ProofStorageKey is null)
            {
                return Result<Payment>.Failure(
                    "This member's registration payment has no proof attached - there's nothing to verify against.");
            }

            return Result<Payment>.Success(existing);
        }

        if (supplied is null)
        {
            return Result<Payment>.Failure(
                "This member has no registration payment on record. Record what they paid before approving.");
        }

        if (supplied.Amount <= 0)
        {
            return Result<Payment>.Failure("Payment amount must be greater than zero.");
        }

        if (supplied.PaidOn > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return Result<Payment>.Failure("Payment date can't be in the future.");
        }

        if (string.IsNullOrWhiteSpace(supplied.ProofStorageKey))
        {
            return Result<Payment>.Failure("Proof of payment is required.");
        }

        // The admin types a raw Amount directly on this path - there's no fee computation to piggy-
        // back PortalFeeAmount onto, unlike EnsureRegistrationPaymentAsync. Still resolved
        // independently here (through the same promotion-aware lookup) so it reflects "what
        // PortalFee was configured at the time," same as Amount is already allowed to diverge from
        // configured fees without validation.
        var portalFeeAmount = supplied.IncludePortalAccess
            ? await FeePromotionResolver.ResolveCurrentAsync(db, MembershipFeeKeys.PortalFee, MembershipFeeKeys.DefaultPortalFee, cancellationToken)
            : 0m;

        var created = new Payment
        {
            MemberId = member.Id,
            Member = member,
            Kind = PaymentKind.NewMembership,
            Amount = supplied.Amount,
            ReferenceNo = supplied.ReferenceNo?.Trim(),
            PaidOn = supplied.PaidOn,
            ProofStorageKey = supplied.ProofStorageKey,
            Status = PaymentStatus.Submitted,
            IncludesPortalAccess = supplied.IncludePortalAccess,
            PortalFeeAmount = portalFeeAmount,
        };
        db.Payments.Add(created);
        return Result<Payment>.Success(created);
    }

    /// <summary>
    /// Covers both cases GetAllAsync's pendingPrcVerificationOnly filter surfaces: a proposed
    /// change to an already-verified value (PendingPrcLicenseNo set - copied into PrcLicenseNo
    /// here), and a first-time, never-reviewed PRC No. (nothing to copy, just marks it verified).
    /// </summary>
    public async Task<Result> ApprovePrcVerificationAsync(Guid memberId, Guid decidedByUserId, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == memberId, cancellationToken);
        if (member is null)
        {
            return Result.NotFound($"Member '{memberId}' was not found.");
        }

        var oldValue = member.PrcLicenseNo;
        var newValue = member.PendingPrcLicenseNo ?? member.PrcLicenseNo;

        if (member.PendingPrcLicenseNo is not null)
        {
            member.PrcLicenseNo = member.PendingPrcLicenseNo;
            member.PendingPrcLicenseNo = null;
        }

        // RegistrationDate/ValidUntilDate stage/commit together with License No. - all three
        // describe the same physical RMP/PRC ID card (see UpsertMyProfileAsync's re-upload gate).
        if (member.PendingPrcRegistrationDate is not null)
        {
            member.PrcRegistrationDate = member.PendingPrcRegistrationDate;
            member.PendingPrcRegistrationDate = null;
        }
        if (member.PendingPrcValidUntilDate is not null)
        {
            member.PrcValidUntilDate = member.PendingPrcValidUntilDate;
            member.PendingPrcValidUntilDate = null;
        }

        member.PrcIdVerified = true;
        member.PrcVerificationRejectedReason = null;
        member.UpdatedAt = DateTimeOffset.UtcNow;

        var upload = await db.MemberUploads.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == member.UserId && u.Kind == UploadKind.PrcId, cancellationToken);

        db.PrcVerificationHistories.Add(new PrcVerificationHistory
        {
            MemberId = member.Id,
            OldValue = oldValue,
            NewValue = newValue,
            DocumentStorageKey = upload?.StorageKey,
            Decision = PrcVerificationDecision.Approved,
            DecidedByUserId = decidedByUserId
        });

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Discards the pending value (if any) and records why - PrcLicenseNo/PrcIdVerified are left
    /// untouched, so an edit-rejection leaves the prior verified value standing, and a
    /// first-time-rejection simply stays unverified with a reason attached (still surfaced by
    /// GetAllAsync's pendingPrcVerificationOnly filter until the member edits again or an admin
    /// later approves).
    /// </summary>
    public async Task<Result> RejectPrcVerificationAsync(Guid memberId, string reason, Guid decidedByUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure("A reason is required to reject a PRC License No. change.");
        }

        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == memberId, cancellationToken);
        if (member is null)
        {
            return Result.NotFound($"Member '{memberId}' was not found.");
        }

        var newValue = member.PendingPrcLicenseNo ?? member.PrcLicenseNo;

        member.PendingPrcLicenseNo = null;
        member.PendingPrcRegistrationDate = null;
        member.PendingPrcValidUntilDate = null;
        member.PrcVerificationRejectedReason = reason;
        member.UpdatedAt = DateTimeOffset.UtcNow;

        var upload = await db.MemberUploads.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == member.UserId && u.Kind == UploadKind.PrcId, cancellationToken);

        db.PrcVerificationHistories.Add(new PrcVerificationHistory
        {
            MemberId = member.Id,
            OldValue = member.PrcLicenseNo,
            NewValue = newValue,
            DocumentStorageKey = upload?.StorageKey,
            Decision = PrcVerificationDecision.Rejected,
            Reason = reason,
            DecidedByUserId = decidedByUserId
        });

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Finalizes the wizard's per-step autosave drafts into a real submitted application.
    /// Idempotent (re-submitting an already-submitted application is a no-op success).
    /// </summary>
    public async Task<Result> SubmitMyProfileAsync(Guid userId, bool includePortalAccess = false, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return Result.NotFound("No draft application was found - complete at least the Personal Information step first.");
        }

        // Step names/order mirror MembershipApplicationWizardCard's `steps` array - each missing
        // field is tagged with the wizard step it's actually collected on, so the failure message
        // can tell the applicant where to go fix it instead of just naming the field.
        const string personalInfoStep = "Personal Information";
        const string contactInfoStep = "Contact Information";
        const string additionalInfoStep = "Additional Information";
        const string paymentDetailsStep = "Payment Details";

        var missing = new List<(string Label, string Step)>();
        if (string.IsNullOrWhiteSpace(member.FirstName)) missing.Add(("First name", personalInfoStep));
        if (string.IsNullOrWhiteSpace(member.LastName)) missing.Add(("Last name", personalInfoStep));
        if (string.IsNullOrWhiteSpace(member.Chapter)) missing.Add(("Chapter", personalInfoStep));
        if (string.IsNullOrWhiteSpace(member.MemberType)) missing.Add(("Member type", personalInfoStep));
        if (string.IsNullOrWhiteSpace(member.PrcLicenseNo)) missing.Add(("RMP License No.", personalInfoStep));
        if (member.PrcRegistrationDate is null) missing.Add(("RMP License Registration Date", personalInfoStep));
        if (member.PrcValidUntilDate is null) missing.Add(("RMP License Valid Until Date", personalInfoStep));
        if (string.IsNullOrWhiteSpace(member.Gender)) missing.Add(("Gender", personalInfoStep));
        if (string.IsNullOrWhiteSpace(member.CivilStatus)) missing.Add(("Civil status", personalInfoStep));
        if (string.IsNullOrWhiteSpace(member.EducationLevel)) missing.Add(("Educational record", personalInfoStep));
        if (string.IsNullOrWhiteSpace(member.SchoolName)) missing.Add(("Name of school/institution", personalInfoStep));
        if (string.IsNullOrWhiteSpace(member.CourseYearGraduated)) missing.Add(("Course & year graduated", personalInfoStep));
        if (string.IsNullOrWhiteSpace(member.SpecifiedProfession)) missing.Add(("Specified profession", personalInfoStep));
        if (member.Birthdate is null) missing.Add(("Birthdate", personalInfoStep));
        // House No. is deliberately not required - some Philippine addresses genuinely don't have
        // one (e.g. rural/informal addresses) - the other 5 residence sub-fields are.
        if (string.IsNullOrWhiteSpace(member.Street)) missing.Add(("Street", contactInfoStep));
        if (string.IsNullOrWhiteSpace(member.Barangay)) missing.Add(("Barangay", contactInfoStep));
        if (string.IsNullOrWhiteSpace(member.CityMunicipality)) missing.Add(("City or Municipality", contactInfoStep));
        if (string.IsNullOrWhiteSpace(member.Province)) missing.Add(("Province", contactInfoStep));
        if (string.IsNullOrWhiteSpace(member.ZipCode)) missing.Add(("Zip code", contactInfoStep));
        // In practice never trips - the wizard pre-selects "Philippines" - but a caller that skips
        // the field entirely (a future non-wizard integration) shouldn't get a countryless address
        // through. MailingCountry is deliberately not required, matching the other mailing fields.
        if (string.IsNullOrWhiteSpace(member.Country)) missing.Add(("Country", contactInfoStep));
        if (string.IsNullOrWhiteSpace(member.MobileNumber)) missing.Add(("Mobile number", contactInfoStep));
        // Nothing on the Additional Information step is required any more - PTR Number was the last
        // one, and it's now optional alongside its place/date and TIN/Company. The step is kept
        // because it still collects those; it just no longer blocks submission.

        var uploadKinds = await db.MemberUploads.AsNoTracking()
            .Where(u => u.UserId == userId).Select(u => u.Kind).ToListAsync(cancellationToken);
        if (!uploadKinds.Contains(UploadKind.PrcId)) missing.Add(("RMP ID document", personalInfoStep));
        if (!uploadKinds.Contains(UploadKind.Photo)) missing.Add(("Photo", personalInfoStep));
        if (!uploadKinds.Contains(UploadKind.ProofOfPayment)) missing.Add(("Proof of payment", paymentDetailsStep));

        if (missing.Count > 0)
        {
            var byStep = missing
                .GroupBy(m => m.Step)
                .OrderBy(g => g.Key switch
                {
                    personalInfoStep => 0,
                    contactInfoStep => 1,
                    additionalInfoStep => 2,
                    _ => 3,
                })
                .Select(g => $"{g.Key} ({string.Join(", ", g.Select(m => m.Label))})");
            return Result.Failure($"Complete the following before submitting: {string.Join("; ", byStep)}.");
        }

        // Checked only once the field is confirmed present above - a missing Birthdate already
        // produced its own message, so this never doubles up with the missing-fields list.
        if (member.Birthdate!.Value > DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-18))
        {
            return Result.Failure("You must be at least 18 years old to submit a membership application.");
        }

        if (member.SubmittedAt is null)
        {
            member.SubmittedAt = DateTimeOffset.UtcNow;
            member.UpdatedAt = DateTimeOffset.UtcNow;
            await EnsureRegistrationPaymentAsync(member, includePortalAccess, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    private static readonly Regex PhMobileNumberPattern = new(@"^(\+63|63|0)9\d{9}$", RegexOptions.Compiled);
    // Lenient PH landline - format varies by area code length (e.g. "(02) 8123 4567", "032-2551234"),
    // so this just checks digits/spaces/dashes/parens with 7-11 digits, not an exact pattern.
    private static readonly Regex HousePhonePattern = new(@"^[\d\s\-()]{7,15}$", RegexOptions.Compiled);

    private static bool IsValidTin(string value)
    {
        var digitsOnly = value.Replace("-", string.Empty);
        return digitsOnly.Length is >= 9 and <= 12 && digitsOnly.All(char.IsDigit);
    }

    private static bool IsValidHousePhone(string value)
    {
        var digitsOnly = value.Count(char.IsDigit);
        return HousePhonePattern.IsMatch(value) && digitsOnly is >= 7 and <= 11;
    }

    /// <summary>
    /// Mirrors the HasMaxLength constraints already declared in MemberConfiguration.cs - without
    /// this, an over-length value falls through to an uncaught DbUpdateException at
    /// SaveChangesAsync (a generic 500) instead of a clean validation failure. Shared by every
    /// write path (self-service and admin) since they all persist the same Member entity.
    /// </summary>
    private static string? ValidateMemberFieldLengths(
        string firstName, string? middleName, string lastName, string? suffix,
        string? civilStatus, string chapter, string memberType,
        string? educationLevel, string? schoolName, string? courseYearGraduated, string? specifiedProfession,
        string? houseNo, string? street, string? barangay, string? cityMunicipality, string? province, string? zipCode, string? country,
        string? mailingHouseNo, string? mailingStreet, string? mailingBarangay, string? mailingCityMunicipality, string? mailingProvince, string? mailingZipCode, string? mailingCountry,
        string? prcLicenseNo, string? ptrNumber, string? ptrPlaceIssued, string? company, string? position,
        string? chapterPosition,
        string? businessAddress, string? specialization, string? skills, string? employmentStatus)
    {
        // Requiredness itself is intentionally NOT checked here - the wizard's per-step autosave
        // (UpsertMyProfileAsync) must tolerate empty values for an in-progress draft; actual
        // required-to-submit enforcement lives in SubmitMyProfileAsync. This only guards length.
        if (firstName.Length > 128) return "First name must be 128 characters or fewer.";
        if (lastName.Length > 128) return "Last name must be 128 characters or fewer.";
        if (middleName?.Length > 128) return "Middle name must be 128 characters or fewer.";
        if (suffix?.Length > 32) return "Suffix must be 32 characters or fewer.";
        if (civilStatus?.Length > 32) return "Civil status must be 32 characters or fewer.";
        if (chapter.Length > 64) return "Chapter must be 64 characters or fewer.";
        if (memberType.Length > 64) return "Member type must be 64 characters or fewer.";
        if (educationLevel?.Length > 32) return "Educational record must be 32 characters or fewer.";
        if (schoolName?.Length > 256) return "Name of school/institution must be 256 characters or fewer.";
        if (courseYearGraduated?.Length > 128) return "Course & year graduated must be 128 characters or fewer.";
        if (specifiedProfession?.Length > 64) return "Specified profession must be 64 characters or fewer.";
        if (prcLicenseNo?.Length > 64) return "RMP License No. must be 64 characters or fewer.";
        if (ptrNumber?.Length > 64) return "PTR number must be 64 characters or fewer.";
        if (company?.Length > 256) return "Company must be 256 characters or fewer.";
        if (position?.Length > 128) return "Position must be 128 characters or fewer.";
        if (businessAddress?.Length > 512) return "Business address must be 512 characters or fewer.";
        if (specialization?.Length > 256) return "Specialization must be 256 characters or fewer.";
        if (skills?.Length > 512) return "Skills must be 512 characters or fewer.";
        if (employmentStatus?.Length > 32) return "Employment status must be 32 characters or fewer.";

        foreach (var (value, label, maxLength) in new (string? Value, string Label, int MaxLength)[]
        {
            (houseNo, "House No.", 32), (street, "Street", 128), (barangay, "Barangay", 128),
            (cityMunicipality, "City/Municipality", 128), (province, "Province", 64), (zipCode, "Zip code", 8), (country, "Country", 64),
            (mailingHouseNo, "Mailing House No.", 32), (mailingStreet, "Mailing Street", 128), (mailingBarangay, "Mailing Barangay", 128),
            (mailingCityMunicipality, "Mailing City/Municipality", 128), (mailingProvince, "Mailing Province", 64), (mailingZipCode, "Mailing Zip code", 8), (mailingCountry, "Mailing Country", 64),
            (chapterPosition, "Chapter position", 128), (ptrPlaceIssued, "PTR place issued", 128),
        })
        {
            if (value?.Length > maxLength) return $"{label} must be {maxLength} characters or fewer.";
        }

        return null;
    }

    public async Task<Result<MemberDto>> UpsertMyProfileAsync(Guid userId, UpdateMyProfileRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.MobileNumber) && !PhMobileNumberPattern.IsMatch(request.MobileNumber))
        {
            return Result<MemberDto>.Failure("Mobile number must be in the format +639XXXXXXXXX, 639XXXXXXXXX, or 09XXXXXXXXX.");
        }

        if (!string.IsNullOrWhiteSpace(request.Tin) && !IsValidTin(request.Tin))
        {
            return Result<MemberDto>.Failure("TIN must be 9-12 digits, with dashes allowed as separators.");
        }

        if (!string.IsNullOrWhiteSpace(request.HousePhone) && !IsValidHousePhone(request.HousePhone))
        {
            return Result<MemberDto>.Failure("House phone must be a valid landline number.");
        }

        if (request.YearsOfPractice is < 0)
        {
            return Result<MemberDto>.Failure("Years of practice cannot be negative.");
        }

        // Sanity range only - an int column can hold anything, so unlike the length checks this
        // isn't guarding against a DbUpdateException. Same self-service-only placement as
        // YearsOfPractice above; the admin paths trust the admin.
        if (request.ChapterYear is < 1900 or > 2200)
        {
            return Result<MemberDto>.Failure("Chapter year must be between 1900 and 2200.");
        }

        var lengthError = ValidateMemberFieldLengths(
            request.FirstName, request.MiddleName, request.LastName, request.Suffix,
            request.CivilStatus, request.Chapter, request.MemberType,
            request.EducationLevel, request.SchoolName, request.CourseYearGraduated, request.SpecifiedProfession,
            request.HouseNo, request.Street, request.Barangay, request.CityMunicipality, request.Province, request.ZipCode, request.Country,
            request.MailingHouseNo, request.MailingStreet, request.MailingBarangay, request.MailingCityMunicipality, request.MailingProvince, request.MailingZipCode, request.MailingCountry,
            request.PrcLicenseNo, request.PtrNumber, request.PtrPlaceIssued, request.Company, request.Position,
            request.ChapterPosition,
            request.BusinessAddress, request.Specialization, request.Skills, request.EmploymentStatus);
        if (lengthError is not null)
        {
            return Result<MemberDto>.Failure(lengthError);
        }

        var member = await db.Members.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        var isDraft = member is null || member.SubmittedAt is null;
        // Captured before any mutation below - this save's own UpdatedAt must never be used as the
        // baseline a re-upload has to beat, or a genuinely fresh upload would always look stale.
        var baselineAt = member is null ? (DateTimeOffset?)null : member.UpdatedAt ?? member.CreatedAt;

        if (member is null)
        {
            // No MembershipNo: PSMPE issues its own control numbers, and an administrator keys one
            // in at approval. The portal used to generate a sequential placeholder here, which
            // showed applicants a number the society had never issued and burned a value for every
            // abandoned draft.
            member = new Member
            {
                UserId = userId,
                Status = MembershipStatus.Pending
            };
            db.Members.Add(member);
        }
        else
        {
            // Once submitted, Member Type and Chapter become admin-managed only - the member could
            // freely choose them while still completing the wizard, but not after. Checked before
            // any mutation so a rejection never partially applies.
            if (!isDraft && (request.MemberType != member.MemberType || request.Chapter != member.Chapter))
            {
                return Result<MemberDto>.Failure("Member Type and Chapter can only be changed by an administrator.");
            }

            member.UpdatedAt = DateTimeOffset.UtcNow;
        }

        // RegistrationDate/ValidUntilDate describe the same physical RMP/PRC ID card as License
        // No. - changing any one of the three requires the same fresh-reupload proof and stages
        // all three together (see Member.PendingPrcRegistrationDate's doc comment).
        var prcLicenseNoChanged = !string.Equals(request.PrcLicenseNo ?? string.Empty, member.PrcLicenseNo ?? string.Empty, StringComparison.Ordinal);
        var prcRegistrationDateChanged = request.PrcRegistrationDate != member.PrcRegistrationDate;
        var prcValidUntilDateChanged = request.PrcValidUntilDate != member.PrcValidUntilDate;
        var prcCardChanged = prcLicenseNoChanged || prcRegistrationDateChanged || prcValidUntilDateChanged;
        if (!isDraft && prcCardChanged)
        {
            // Changing an already-submitted application's RMP License No./dates requires proof a
            // new RMP ID was just uploaded - the client's PrcIdReuploaded flag alone isn't trusted,
            // since a crafted request could set it without actually re-uploading anything.
            if (!request.PrcIdReuploaded)
            {
                return Result<MemberDto>.Failure("Changing your RMP License No. or dates requires uploading a new RMP ID document first.");
            }

            var upload = await db.MemberUploads.AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId && u.Kind == UploadKind.PrcId, cancellationToken);
            var uploadedAt = upload?.UpdatedAt ?? upload?.CreatedAt;
            if (upload is null || uploadedAt <= baselineAt)
            {
                return Result<MemberDto>.Failure("Changing your RMP License No. or dates requires uploading a new RMP ID document first.");
            }

            // Stage the proposed values rather than overwriting directly - they only become
            // current once an admin approves (MemberService.ApprovePrcVerificationAsync). A fresh
            // attempt supersedes any earlier rejection message.
            member.PendingPrcLicenseNo = request.PrcLicenseNo;
            member.PendingPrcRegistrationDate = request.PrcRegistrationDate;
            member.PendingPrcValidUntilDate = request.PrcValidUntilDate;
            member.PrcVerificationRejectedReason = null;
        }

        member.FirstName = request.FirstName;
        member.MiddleName = request.MiddleName;
        member.LastName = request.LastName;
        member.Suffix = request.Suffix;
        member.Birthdate = request.Birthdate;
        member.Gender = request.Gender;
        member.CivilStatus = request.CivilStatus;
        member.EducationLevel = request.EducationLevel;
        member.SchoolName = request.SchoolName;
        member.CourseYearGraduated = request.CourseYearGraduated;
        member.SpecifiedProfession = request.SpecifiedProfession;
        member.MobileNumber = request.MobileNumber;
        member.HouseNo = request.HouseNo;
        member.Street = request.Street;
        member.Barangay = request.Barangay;
        member.CityMunicipality = request.CityMunicipality;
        member.Province = request.Province;
        member.ZipCode = request.ZipCode;
        member.Country = request.Country;
        member.MailingHouseNo = request.MailingHouseNo;
        member.MailingStreet = request.MailingStreet;
        member.MailingBarangay = request.MailingBarangay;
        member.MailingCityMunicipality = request.MailingCityMunicipality;
        member.MailingProvince = request.MailingProvince;
        member.MailingZipCode = request.MailingZipCode;
        member.MailingCountry = request.MailingCountry;
        member.HousePhone = request.HousePhone;
        member.PtrNumber = request.PtrNumber;
        member.PtrPlaceIssued = request.PtrPlaceIssued;
        member.PtrDateIssued = request.PtrDateIssued;
        member.Tin = request.Tin;
        // Chapter officer details describe a post the member holds, not their eligibility, so they
        // sit outside the !isDraft Chapter/MemberType lock below - a member elected mid-term
        // records it themselves.
        member.ChapterYear = request.ChapterYear;
        member.ChapterPosition = request.ChapterPosition;
        // Professional Information is entirely optional and never locked post-submission - a
        // member can fill it in (or change it) at any time, unlike MemberType/Chapter/PrcLicenseNo.
        member.EmploymentStatus = request.EmploymentStatus;
        member.Company = request.Company;
        member.Position = request.Position;
        member.BusinessAddress = request.BusinessAddress;
        member.YearsOfPractice = request.YearsOfPractice;
        member.Specialization = request.Specialization;
        member.Skills = request.Skills;
        if (isDraft)
        {
            // Free entry during the wizard, no reupload gating or staging - matches today.
            member.PrcLicenseNo = request.PrcLicenseNo;
            member.PrcRegistrationDate = request.PrcRegistrationDate;
            member.PrcValidUntilDate = request.PrcValidUntilDate;
            member.Chapter = request.Chapter;
            member.MemberType = request.MemberType;
        }

        await db.SaveChangesAsync(cancellationToken);
        var dto = await GetByIdAsync(member.Id, cancellationToken) ?? throw new InvalidOperationException("Member was not persisted.");
        return Result<MemberDto>.Success(dto);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (member is null)
        {
            return Result.NotFound($"Member '{id}' was not found.");
        }

        // PrcVerificationHistory.MemberId is a Restrict FK - deleting a member with any PRC
        // verification history would otherwise throw a raw DbUpdateException; checked here so it
        // surfaces as a clean, expected failure instead.
        if (await db.PrcVerificationHistories.AnyAsync(h => h.MemberId == id, cancellationToken))
        {
            return Result.Failure("Cannot delete a member with PRC verification history.");
        }

        // Payment.MemberId is a Restrict FK for the same reason - a financial record must not be
        // swept away with the member it belongs to.
        if (await db.Payments.AnyAsync(p => p.MemberId == id, cancellationToken))
        {
            return Result.Failure("Cannot delete a member with payment history.");
        }

        db.Members.Remove(member);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<bool> HasPrcVerificationHistoryAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var memberId = await db.Members.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (memberId is null)
        {
            return false;
        }

        return await db.PrcVerificationHistories.AnyAsync(h => h.MemberId == memberId, cancellationToken);
    }

    /// <summary>
    /// Makes sure a freshly submitted application has a NewMembership Payment for an admin to
    /// verify, carrying over the Proof of Payment the wizard already required.
    ///
    /// Done here rather than by changing the submit gate itself so an older client that never calls
    /// POST /api/payments/me still produces a verifiable record - without this, an application could
    /// be approved and then have no payment to activate it. If the member already declared one
    /// (amount, reference, date) through the payments endpoint, that row is left alone and only its
    /// proof is filled in.
    /// </summary>
    private async Task EnsureRegistrationPaymentAsync(Member member, bool includePortalAccess, CancellationToken cancellationToken)
    {
        var proofKey = await db.MemberUploads.AsNoTracking()
            .Where(u => u.UserId == member.UserId && u.Kind == UploadKind.ProofOfPayment)
            .Select(u => u.StorageKey)
            .FirstOrDefaultAsync(cancellationToken);

        var existing = await db.Payments
            .FirstOrDefaultAsync(p => p.MemberId == member.Id && p.Kind == PaymentKind.NewMembership, cancellationToken);

        if (existing is not null)
        {
            existing.ProofStorageKey ??= proofKey;
            return;
        }

        // Read inline rather than through IPaymentService: MemberService is constructed as
        // `new MemberService(db)` in ~70 unit tests, and adding a required dependency to satisfy
        // one default value would break all of them for no benefit.
        var feeRows = await db.SystemConfigs.AsNoTracking()
            .Where(c => c.Key == MembershipFeeKeys.MembershipFee || c.Key == MembershipFeeKeys.ShippingFee || c.Key == MembershipFeeKeys.PortalFee)
            .ToDictionaryAsync(c => c.Key, c => c.Value, cancellationToken);

        // Resolved through FeePromotionResolver rather than the plain configured value, so a
        // promotion applies here the same way it does in PaymentService.GetFeesAsync. No cache
        // parameter needed: the resolver does no caching of its own (callers decide), and this
        // method already reads SystemConfig uncached above, so there is nothing to keep consistent
        // with by adding one.
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        var membershipFee = await FeePromotionResolver.ResolveAsync(
            db, MembershipFeeKeys.MembershipFee,
            ReadFee(feeRows, MembershipFeeKeys.MembershipFee, MembershipFeeKeys.DefaultMembershipFee), asOf, cancellationToken);
        var shippingFee = await FeePromotionResolver.ResolveAsync(
            db, MembershipFeeKeys.ShippingFee,
            ReadFee(feeRows, MembershipFeeKeys.ShippingFee, MembershipFeeKeys.DefaultShippingFee), asOf, cancellationToken);
        var portalFee = await FeePromotionResolver.ResolveAsync(
            db, MembershipFeeKeys.PortalFee,
            ReadFee(feeRows, MembershipFeeKeys.PortalFee, MembershipFeeKeys.DefaultPortalFee), asOf, cancellationToken);

        db.Payments.Add(new Payment
        {
            MemberId = member.Id,
            Kind = PaymentKind.NewMembership,
            // What PSMPE charges, not what the member typed - they didn't declare an amount on this
            // path. The admin sees the proof and can reject if it doesn't match. Portal access adds
            // its fee on top only when the applicant ticked the opt-in checkbox.
            Amount = membershipFee + shippingFee + (includePortalAccess ? portalFee : 0m),
            PaidOn = DateOnly.FromDateTime(DateTime.UtcNow),
            ProofStorageKey = proofKey,
            Status = PaymentStatus.Submitted,
            IncludesPortalAccess = includePortalAccess,
            // Same resolved value already used above to size Amount - captured independently so a
            // later fee/promo edit can never retroactively change what this payment's own
            // portal-revenue contribution was.
            PortalFeeAmount = includePortalAccess ? portalFee : 0m,
        });
    }

    private static decimal ReadFee(IReadOnlyDictionary<string, string> rows, string key, decimal fallback) =>
        rows.TryGetValue(key, out var raw)
            && decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    /// <summary>
    /// Baseline 50% once submitted (Steps 1-3 registration is done) plus up to 50% more split
    /// evenly across 4 optional completeness signals: Professional Information, Valid Government
    /// ID, Signature, and at least one Certificate. Deliberately excludes PRC ID and Photo from
    /// this optional split - both are already required to submit, so they're always true by the
    /// time this can return a non-zero percent.
    /// </summary>
    public async Task<ProfileCompletenessDto?> GetProfileCompletenessAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return null;
        }

        var uploadKinds = await db.MemberUploads.AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => u.Kind)
            .ToListAsync(cancellationToken);
        var hasPrcId = uploadKinds.Contains(UploadKind.PrcId);
        var hasValidGovernmentId = uploadKinds.Contains(UploadKind.ValidGovernmentId);
        var hasPhoto = uploadKinds.Contains(UploadKind.Photo);
        var hasSignature = uploadKinds.Contains(UploadKind.Signature);
        var hasProofOfPayment = uploadKinds.Contains(UploadKind.ProofOfPayment);
        var certificateCount = await db.MemberCertificates.AsNoTracking().CountAsync(c => c.UserId == userId, cancellationToken);

        var hasProfessionalInfo = !string.IsNullOrWhiteSpace(member.EmploymentStatus)
            || !string.IsNullOrWhiteSpace(member.Position)
            || !string.IsNullOrWhiteSpace(member.BusinessAddress)
            || member.YearsOfPractice is not null
            || !string.IsNullOrWhiteSpace(member.Specialization)
            || !string.IsNullOrWhiteSpace(member.Skills);

        const int baselinePercent = 50;
        var isSubmitted = member.SubmittedAt is not null;
        // Photo and ProofOfPayment are excluded from this optional split, same as PrcId - all
        // three are required to submit (see SubmitMyProfileAsync), so they're always true by the
        // time this can return a non-zero percent.
        var optionalItemsDone = new[] { hasProfessionalInfo, hasValidGovernmentId, hasSignature, certificateCount > 0 }.Count(x => x);
        var percent = isSubmitted ? baselinePercent + (int)Math.Round(optionalItemsDone / 4.0 * (100 - baselinePercent)) : 0;

        return new ProfileCompletenessDto(
            percent, isSubmitted, hasPrcId, hasValidGovernmentId, hasPhoto, hasSignature, hasProofOfPayment, certificateCount, hasProfessionalInfo);
    }
}
