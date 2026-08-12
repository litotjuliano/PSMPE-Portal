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
    public string? MobileNumber { get; set; }

    // Residence address (wizard Step 2 / Contact Information) - structured to match the official
    // PSMPE Membership Application Form, replacing the old single free-text Address field.
    public string? HouseNo { get; set; }
    public string? Street { get; set; }
    public string? Barangay { get; set; }
    public string? CityMunicipality { get; set; }
    public string? Province { get; set; }
    public string? ZipCode { get; set; }
    /// <summary>Defaults to "Philippines" in the UI but is not locked - a member can legitimately
    /// reside overseas. Region/Province/City are Philippine-specific and left blank in that case.</summary>
    public string? Country { get; set; }

    // Mailing address - defaults to a copy of the residence address client-side when the
    // applicant checks "Same as Residence Address"; no separate flag is stored since these are
    // just plain fields once submitted.
    public string? MailingHouseNo { get; set; }
    public string? MailingStreet { get; set; }
    public string? MailingBarangay { get; set; }
    public string? MailingCityMunicipality { get; set; }
    public string? MailingProvince { get; set; }
    public string? MailingZipCode { get; set; }
    public string? MailingCountry { get; set; }

    // Educational record (wizard Step 1 / Personal Information).
    public string? EducationLevel { get; set; }
    public string? SchoolName { get; set; }
    public string? CourseYearGraduated { get; set; }

    // Specified profession - distinct from MemberType (e.g. "Master Plumber" vs "Other
    // Professional Related"), per the application form.
    public string? SpecifiedProfession { get; set; }

    // Contact Information (wizard Step 2) - all optional.
    public string? HousePhone { get; set; }

    /// <summary>
    /// PSMPE's own control number, keyed in by an administrator at approval - the portal never
    /// generates one. Null until then, which is why the column is nullable despite carrying a
    /// unique index: Postgres permits many NULLs under a unique index, so unassigned applicants
    /// don't collide with each other.
    /// </summary>
    public string? MembershipNo { get; set; }

    /// <summary>Displayed to applicants as "RMP License No." (Registered Master Plumber) - same
    /// field/workflow as before, only the user-facing label changed; internal naming stays PRC*
    /// throughout the codebase.</summary>
    public string? PrcLicenseNo { get; set; }
    public DateOnly? PrcRegistrationDate { get; set; }
    public DateOnly? PrcValidUntilDate { get; set; }
    public string? PtrNumber { get; set; }
    /// <summary>Where and when the PTR was issued, straight off the receipt. Both optional - a
    /// PTR Number on its own is still enough to submit an application.</summary>
    public string? PtrPlaceIssued { get; set; }
    public DateOnly? PtrDateIssued { get; set; }
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
    /// already-submitted application changes PrcLicenseNo, PrcRegistrationDate, or
    /// PrcValidUntilDate (with a fresh PRC ID reupload) - all three stage/commit/discard together
    /// as one unit, since they describe the same physical RMP/PRC ID card. Null means no change is
    /// pending. PrcLicenseNo itself is NOT overwritten until an admin approves - see
    /// MemberService.UpsertMyProfileAsync/ApprovePrcVerificationAsync.
    /// </summary>
    public string? PendingPrcLicenseNo { get; set; }
    public DateOnly? PendingPrcRegistrationDate { get; set; }
    public DateOnly? PendingPrcValidUntilDate { get; set; }

    /// <summary>
    /// Set when an admin rejects a pending PrcLicenseNo change, shown to the member until they
    /// attempt another PRC change (which clears it, whether or not that new attempt is itself
    /// later approved).
    /// </summary>
    public string? PrcVerificationRejectedReason { get; set; }

    public string Chapter { get; set; } = string.Empty;
    public string MemberType { get; set; } = string.Empty;

    /// <summary>
    /// Optional record of an officer post held in the member's chapter - ChapterPosition is the
    /// role ("Secretary"), ChapterYear the year it was held. Named Chapter* because Position
    /// already means the member's *employment* position further down. Unlike Chapter/MemberType
    /// these are never locked post-submission: a member elected mid-term records it themselves.
    /// </summary>
    public int? ChapterYear { get; set; }
    public string? ChapterPosition { get; set; }

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
