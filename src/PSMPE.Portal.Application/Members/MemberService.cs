using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Members.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Members;

public class MemberService(IApplicationDbContext db) : IMemberService
{
    private const string GracePeriodConfigKey = "MembershipGracePeriodDays";
    private const int DefaultGracePeriodDays = 30;

    private async Task<int> GetGracePeriodDaysAsync(CancellationToken cancellationToken)
    {
        var config = await db.SystemConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == GracePeriodConfigKey, cancellationToken);
        return config is not null && int.TryParse(config.Value, out var days) ? days : DefaultGracePeriodDays;
    }

    /// <summary>
    /// A member keeps limited portal access for GracePeriodDays after RenewalDueDate lapses,
    /// rather than losing access the instant it passes.
    /// </summary>
    private static bool ComputeIsInGracePeriod(Member m, int gracePeriodDays)
    {
        if (m.Status != MembershipStatus.Active || m.RenewalDueDate is null)
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

    private static MemberDto ToDto(Member m, int gracePeriodDays) => new(
        m.Id, m.UserId, m.User.Email ?? string.Empty, m.FirstName, m.MiddleName, m.LastName, m.Suffix,
        m.Birthdate, m.Gender, m.CivilStatus, m.Address, m.MobileNumber,
        m.HousePhone, m.Website, m.FacebookUrl, m.LinkedInUrl, m.XUrl, m.InstagramUrl,
        m.MembershipNo, m.PrcLicenseNo,
        m.PtrNumber, m.Tin, m.PrcIdVerified,
        m.PendingPrcLicenseNo, m.PrcVerificationRejectedReason, m.Chapter,
        m.EmploymentStatus, m.Company, m.Position, m.BusinessAddress, m.YearsOfPractice, m.Specialization, m.Skills,
        m.MemberType,
        m.Status, m.RenewalDueDate, m.NationalDuesReferenceNo, m.ApprovedAt, m.SubmittedAt,
        ComputeIsInGracePeriod(m, gracePeriodDays), m.CreatedAt, m.UpdatedAt);

    public async Task<PagedResult<MemberDto>> GetAllAsync(
        int page, int pageSize, string sortBy, string sortDir, MembershipStatus? status,
        bool? pendingApprovalOnly = null, bool? pendingPrcVerificationOnly = null,
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
            // Two cases: a proposed change to an already-verified value (PendingPrcLicenseNo set),
            // or a PRC No. that has never been reviewed at all (submitted at registration, never
            // since verified or changed) - both need an admin decision, so both show up here.
            query = query.Where(m => m.PendingPrcLicenseNo != null
                || (!m.PrcIdVerified && m.PrcLicenseNo != null && m.PendingPrcLicenseNo == null));
        }

        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = sortBy.ToLowerInvariant() switch
        {
            "membershipno" => descending ? query.OrderByDescending(m => m.MembershipNo) : query.OrderBy(m => m.MembershipNo),
            "chapter" => descending ? query.OrderByDescending(m => m.Chapter) : query.OrderBy(m => m.Chapter),
            "status" => descending ? query.OrderByDescending(m => m.Status) : query.OrderBy(m => m.Status),
            _ => descending ? query.OrderByDescending(m => m.LastName) : query.OrderBy(m => m.LastName),
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var gracePeriodDays = await GetGracePeriodDaysAsync(cancellationToken);
        return new PagedResult<MemberDto>(items.Select(m => ToDto(m, gracePeriodDays)).ToList(), totalCount, page, pageSize);
    }

    public async Task<MemberDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.AsNoTracking().Include(m => m.User).FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (member is null)
        {
            return null;
        }

        var gracePeriodDays = await GetGracePeriodDaysAsync(cancellationToken);
        return ToDto(member, gracePeriodDays);
    }

    public async Task<MemberDto?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.AsNoTracking().Include(m => m.User).FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return null;
        }

        var gracePeriodDays = await GetGracePeriodDaysAsync(cancellationToken);
        return ToDto(member, gracePeriodDays);
    }

    public Task<bool> MembershipNoExistsAsync(string membershipNo, CancellationToken cancellationToken = default) =>
        db.Members.AsNoTracking().AnyAsync(m => m.MembershipNo == membershipNo, cancellationToken);

    public async Task<Result<MemberDto>> CreateAsync(CreateMemberRequest request, CancellationToken cancellationToken = default)
    {
        var lengthError = ValidateMemberFieldLengths(
            request.FirstName, request.MiddleName, request.LastName, request.Suffix,
            request.CivilStatus, request.Address, request.Chapter, request.MemberType,
            request.PrcLicenseNo, request.PtrNumber, request.Company, request.Position,
            request.BusinessAddress, request.Specialization, request.Skills, request.EmploymentStatus,
            request.Website, request.FacebookUrl, request.LinkedInUrl, request.XUrl, request.InstagramUrl);
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
            Address = request.Address,
            MobileNumber = request.MobileNumber,
            HousePhone = request.HousePhone,
            Website = request.Website,
            FacebookUrl = request.FacebookUrl,
            LinkedInUrl = request.LinkedInUrl,
            XUrl = request.XUrl,
            InstagramUrl = request.InstagramUrl,
            PrcLicenseNo = request.PrcLicenseNo,
            PtrNumber = request.PtrNumber,
            Tin = request.Tin,
            Chapter = request.Chapter,
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
            request.CivilStatus, request.Address, request.Chapter, request.MemberType,
            request.PrcLicenseNo, request.PtrNumber, request.Company, request.Position,
            request.BusinessAddress, request.Specialization, request.Skills, request.EmploymentStatus,
            request.Website, request.FacebookUrl, request.LinkedInUrl, request.XUrl, request.InstagramUrl);
        if (lengthError is not null)
        {
            return Result.Failure(lengthError);
        }

        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (member is null)
        {
            return Result.NotFound($"Member '{id}' was not found.");
        }

        member.FirstName = request.FirstName;
        member.MiddleName = request.MiddleName;
        member.LastName = request.LastName;
        member.Suffix = request.Suffix;
        member.Birthdate = request.Birthdate;
        member.Gender = request.Gender;
        member.CivilStatus = request.CivilStatus;
        member.Address = request.Address;
        member.MobileNumber = request.MobileNumber;
        member.HousePhone = request.HousePhone;
        member.Website = request.Website;
        member.FacebookUrl = request.FacebookUrl;
        member.LinkedInUrl = request.LinkedInUrl;
        member.XUrl = request.XUrl;
        member.InstagramUrl = request.InstagramUrl;
        member.PrcLicenseNo = request.PrcLicenseNo;
        member.PtrNumber = request.PtrNumber;
        member.Tin = request.Tin;
        member.Chapter = request.Chapter;
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

    /// <summary>Idempotent - approving an already-approved application is a no-op success, not an error.</summary>
    public async Task<Result> ApproveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (member is null)
        {
            return Result.NotFound($"Member '{id}' was not found.");
        }

        if (member.ApprovedAt is null)
        {
            member.ApprovedAt = DateTimeOffset.UtcNow;
            member.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
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
    public async Task<Result> SubmitMyProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return Result.NotFound("No draft application was found - complete at least the Personal Information step first.");
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(member.FirstName)) missing.Add("First name");
        if (string.IsNullOrWhiteSpace(member.LastName)) missing.Add("Last name");
        if (string.IsNullOrWhiteSpace(member.Chapter)) missing.Add("Chapter");
        if (string.IsNullOrWhiteSpace(member.MemberType)) missing.Add("Member type");
        if (string.IsNullOrWhiteSpace(member.PrcLicenseNo)) missing.Add("PRC License No.");
        if (string.IsNullOrWhiteSpace(member.Gender)) missing.Add("Gender");
        if (string.IsNullOrWhiteSpace(member.CivilStatus)) missing.Add("Civil status");
        if (string.IsNullOrWhiteSpace(member.Address)) missing.Add("Address");
        if (string.IsNullOrWhiteSpace(member.MobileNumber)) missing.Add("Mobile number");
        if (string.IsNullOrWhiteSpace(member.PtrNumber)) missing.Add("PTR number");
        if (member.Birthdate is null) missing.Add("Birthdate");
        if (!await db.MemberUploads.AnyAsync(u => u.UserId == userId && u.Kind == UploadKind.PrcId, cancellationToken))
        {
            missing.Add("PRC ID document");
        }
        if (missing.Count > 0)
        {
            return Result.Failure($"Complete the following before submitting: {string.Join(", ", missing)}.");
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
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    private static readonly Regex PhMobileNumberPattern = new(@"^(\+63|0)9\d{9}$", RegexOptions.Compiled);
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

    private static bool IsValidUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Mirrors the HasMaxLength constraints already declared in MemberConfiguration.cs - without
    /// this, an over-length value falls through to an uncaught DbUpdateException at
    /// SaveChangesAsync (a generic 500) instead of a clean validation failure. Shared by every
    /// write path (self-service and admin) since they all persist the same Member entity.
    /// </summary>
    private static string? ValidateMemberFieldLengths(
        string firstName, string? middleName, string lastName, string? suffix,
        string? civilStatus, string? address, string chapter, string memberType,
        string? prcLicenseNo, string? ptrNumber, string? company, string? position,
        string? businessAddress, string? specialization, string? skills, string? employmentStatus,
        string? website, string? facebookUrl, string? linkedInUrl, string? xUrl, string? instagramUrl)
    {
        // Requiredness itself is intentionally NOT checked here - the wizard's per-step autosave
        // (UpsertMyProfileAsync) must tolerate empty values for an in-progress draft; actual
        // required-to-submit enforcement lives in SubmitMyProfileAsync. This only guards length.
        if (firstName.Length > 128) return "First name must be 128 characters or fewer.";
        if (lastName.Length > 128) return "Last name must be 128 characters or fewer.";
        if (middleName?.Length > 128) return "Middle name must be 128 characters or fewer.";
        if (suffix?.Length > 32) return "Suffix must be 32 characters or fewer.";
        if (civilStatus?.Length > 32) return "Civil status must be 32 characters or fewer.";
        if (address?.Length > 512) return "Address must be 512 characters or fewer.";
        if (chapter.Length > 64) return "Chapter must be 64 characters or fewer.";
        if (memberType.Length > 64) return "Member type must be 64 characters or fewer.";
        if (prcLicenseNo?.Length > 64) return "PRC License No. must be 64 characters or fewer.";
        if (ptrNumber?.Length > 64) return "PTR number must be 64 characters or fewer.";
        if (company?.Length > 256) return "Company must be 256 characters or fewer.";
        if (position?.Length > 128) return "Position must be 128 characters or fewer.";
        if (businessAddress?.Length > 512) return "Business address must be 512 characters or fewer.";
        if (specialization?.Length > 256) return "Specialization must be 256 characters or fewer.";
        if (skills?.Length > 512) return "Skills must be 512 characters or fewer.";
        if (employmentStatus?.Length > 32) return "Employment status must be 32 characters or fewer.";

        foreach (var (value, label) in new (string? Value, string Label)[]
        {
            (website, "Website"), (facebookUrl, "Facebook URL"), (linkedInUrl, "LinkedIn URL"),
            (xUrl, "X URL"), (instagramUrl, "Instagram URL"),
        })
        {
            if (value?.Length > 256) return $"{label} must be 256 characters or fewer.";
        }

        return null;
    }

    public async Task<Result<MemberDto>> UpsertMyProfileAsync(Guid userId, UpdateMyProfileRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.MobileNumber) && !PhMobileNumberPattern.IsMatch(request.MobileNumber))
        {
            return Result<MemberDto>.Failure("Mobile number must be in the format +639XXXXXXXXX or 09XXXXXXXXX.");
        }

        if (!string.IsNullOrWhiteSpace(request.Tin) && !IsValidTin(request.Tin))
        {
            return Result<MemberDto>.Failure("TIN must be 9-12 digits, with dashes allowed as separators.");
        }

        if (!string.IsNullOrWhiteSpace(request.HousePhone) && !IsValidHousePhone(request.HousePhone))
        {
            return Result<MemberDto>.Failure("House phone must be a valid landline number.");
        }

        var urlFields = new (string? Value, string Label)[]
        {
            (request.Website, "Website"), (request.FacebookUrl, "Facebook URL"), (request.LinkedInUrl, "LinkedIn URL"),
            (request.XUrl, "X URL"), (request.InstagramUrl, "Instagram URL"),
        };
        foreach (var (value, label) in urlFields)
        {
            if (!string.IsNullOrWhiteSpace(value) && !IsValidUrl(value))
            {
                return Result<MemberDto>.Failure($"{label} must be a valid URL (starting with http:// or https://).");
            }
        }

        if (request.YearsOfPractice is < 0)
        {
            return Result<MemberDto>.Failure("Years of practice cannot be negative.");
        }

        var lengthError = ValidateMemberFieldLengths(
            request.FirstName, request.MiddleName, request.LastName, request.Suffix,
            request.CivilStatus, request.Address, request.Chapter, request.MemberType,
            request.PrcLicenseNo, request.PtrNumber, request.Company, request.Position,
            request.BusinessAddress, request.Specialization, request.Skills, request.EmploymentStatus,
            request.Website, request.FacebookUrl, request.LinkedInUrl, request.XUrl, request.InstagramUrl);
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
            member = new Member
            {
                UserId = userId,
                MembershipNo = await GenerateMembershipNoAsync(cancellationToken),
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

        var prcLicenseNoChanged = !string.Equals(request.PrcLicenseNo ?? string.Empty, member.PrcLicenseNo ?? string.Empty, StringComparison.Ordinal);
        if (!isDraft && prcLicenseNoChanged)
        {
            // Changing an already-submitted application's PRC License No. requires proof a new PRC
            // ID was just uploaded - the client's PrcIdReuploaded flag alone isn't trusted, since a
            // crafted request could set it without actually re-uploading anything.
            if (!request.PrcIdReuploaded)
            {
                return Result<MemberDto>.Failure("Changing your PRC License No. requires uploading a new PRC ID document first.");
            }

            var upload = await db.MemberUploads.AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId && u.Kind == UploadKind.PrcId, cancellationToken);
            var uploadedAt = upload?.UpdatedAt ?? upload?.CreatedAt;
            if (upload is null || uploadedAt <= baselineAt)
            {
                return Result<MemberDto>.Failure("Changing your PRC License No. requires uploading a new PRC ID document first.");
            }

            // Stage the proposed value rather than overwriting PrcLicenseNo directly - it only
            // becomes current once an admin approves (MemberService.ApprovePrcVerificationAsync).
            // A fresh attempt supersedes any earlier rejection message.
            member.PendingPrcLicenseNo = request.PrcLicenseNo;
            member.PrcVerificationRejectedReason = null;
        }

        member.FirstName = request.FirstName;
        member.MiddleName = request.MiddleName;
        member.LastName = request.LastName;
        member.Suffix = request.Suffix;
        member.Birthdate = request.Birthdate;
        member.Gender = request.Gender;
        member.CivilStatus = request.CivilStatus;
        member.Address = request.Address;
        member.MobileNumber = request.MobileNumber;
        member.HousePhone = request.HousePhone;
        member.Website = request.Website;
        member.FacebookUrl = request.FacebookUrl;
        member.LinkedInUrl = request.LinkedInUrl;
        member.XUrl = request.XUrl;
        member.InstagramUrl = request.InstagramUrl;
        member.PtrNumber = request.PtrNumber;
        member.Tin = request.Tin;
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

        db.Members.Remove(member);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Simple sequential scheme, zero-padded to 6 digits. Not perfectly race-safe under concurrent
    /// first-time self-registrations, but the unique index on MembershipNo guarantees no duplicate
    /// ever gets persisted - a rare collision just surfaces as a SaveChanges failure to retry.
    /// </summary>
    private async Task<string> GenerateMembershipNoAsync(CancellationToken cancellationToken)
    {
        var count = await db.Members.CountAsync(cancellationToken);
        return (count + 1).ToString("D6");
    }

    /// <summary>
    /// Baseline 50% once submitted (Steps 1-3 registration is done) plus up to 50% more split
    /// evenly across 5 optional completeness signals: Professional Information, Valid Government
    /// ID, 2x2 Formal Photo, Signature, and at least one Certificate. Deliberately excludes PRC ID
    /// from this optional split - that one is already required to submit, so it's always true by
    /// the time this can return a non-zero percent.
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
        var hasFormalPhoto = uploadKinds.Contains(UploadKind.FormalPhoto);
        var hasSignature = uploadKinds.Contains(UploadKind.Signature);
        var certificateCount = await db.MemberCertificates.AsNoTracking().CountAsync(c => c.UserId == userId, cancellationToken);

        var hasProfessionalInfo = !string.IsNullOrWhiteSpace(member.EmploymentStatus)
            || !string.IsNullOrWhiteSpace(member.Position)
            || !string.IsNullOrWhiteSpace(member.BusinessAddress)
            || member.YearsOfPractice is not null
            || !string.IsNullOrWhiteSpace(member.Specialization)
            || !string.IsNullOrWhiteSpace(member.Skills);

        const int baselinePercent = 50;
        var isSubmitted = member.SubmittedAt is not null;
        var optionalItemsDone = new[] { hasProfessionalInfo, hasValidGovernmentId, hasFormalPhoto, hasSignature, certificateCount > 0 }.Count(x => x);
        var percent = isSubmitted ? baselinePercent + (int)Math.Round(optionalItemsDone / 5.0 * (100 - baselinePercent)) : 0;

        return new ProfileCompletenessDto(
            percent, isSubmitted, hasPrcId, hasValidGovernmentId, hasFormalPhoto, hasSignature, certificateCount, hasProfessionalInfo);
    }
}
