using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// A PSMPE professional membership profile - distinct from ApplicationUser (the login/role
/// account). Every Member has exactly one linked ApplicationUser (1:1, required), but not every
/// ApplicationUser has a Member profile - staff accounts (Admin/Manager/Accounts) manage the
/// system without necessarily being licensed engineers with a membership record.
/// </summary>
public class Member : BaseEntity
{
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string? Suffix { get; set; }

    public DateOnly? Birthdate { get; set; }
    public string? Gender { get; set; }
    public string? CivilStatus { get; set; }
    public string? Address { get; set; }
    public string? MobileNumber { get; set; }

    // Contact Information (wizard Step 2) - all optional.
    public string? HousePhone { get; set; }
    public string? Website { get; set; }
    public string? FacebookUrl { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? XUrl { get; set; }
    public string? InstagramUrl { get; set; }

    public string MembershipNo { get; set; } = string.Empty;
    public string? PrcLicenseNo { get; set; }
    public string? PtrNumber { get; set; }
    public string? Tin { get; set; }

    /// <summary>
    /// Whether an admin has reviewed and approved the member's current PrcLicenseNo/PRC ID
    /// document. Only ever set by MemberService.ApprovePrcVerificationAsync - never by the member
    /// themselves, and never by a raw admin toggle (see PrcVerificationHistory for the decision
    /// log).
    /// </summary>
    public bool PrcIdVerified { get; set; }

    /// <summary>
    /// A proposed new PrcLicenseNo awaiting an admin decision - set when a member with an
    /// already-submitted application changes PrcLicenseNo (with a fresh PRC ID reupload). Null
    /// means no change is pending. PrcLicenseNo itself is NOT overwritten until an admin approves
    /// - see MemberService.UpsertMyProfileAsync/ApprovePrcVerificationAsync.
    /// </summary>
    public string? PendingPrcLicenseNo { get; set; }

    /// <summary>
    /// Set when an admin rejects a pending PrcLicenseNo change, shown to the member until they
    /// attempt another PRC change (which clears it, whether or not that new attempt is itself
    /// later approved).
    /// </summary>
    public string? PrcVerificationRejectedReason { get; set; }

    public string Chapter { get; set; } = string.Empty;
    public string MemberType { get; set; } = string.Empty;

    // Professional Information - post-approval, entirely optional (see My Profile's Professional
    // Information tab). EmploymentStatus gates which of Company/Position/BusinessAddress are
    // meaningful (Employed -> Company+Position; Self-Employed/Business Owner -> BusinessAddress),
    // enforced client-side only - the server never requires any of these.
    public string? EmploymentStatus { get; set; }
    public string? Company { get; set; }
    public string? Position { get; set; }
    public string? BusinessAddress { get; set; }
    public int? YearsOfPractice { get; set; }
    public string? Specialization { get; set; }
    public string? Skills { get; set; }

    public MembershipStatus Status { get; set; } = MembershipStatus.Pending;
    public DateOnly? RenewalDueDate { get; set; }
    public string? NationalDuesReferenceNo { get; set; }

    /// <summary>
    /// When an admin approved this application. Null means "not yet reviewed" - a distinct axis
    /// from Status, since an approved application can still be Pending until dues are paid.
    /// </summary>
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>
    /// When the applicant finished the multi-step registration wizard and submitted it for
    /// review. Null means this is still an in-progress draft (created by the wizard's per-step
    /// autosave) - drafts are invisible to admins entirely, not just unapproved.
    /// </summary>
    public DateTimeOffset? SubmittedAt { get; set; }
}
