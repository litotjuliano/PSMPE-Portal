using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Members.Dtos;

public record MemberDto(
    Guid Id,
    Guid UserId,
    string Email,
    string FirstName,
    string? MiddleName,
    string LastName,
    string? Suffix,
    DateOnly? Birthdate,
    string? Gender,
    string? CivilStatus,
    string? EducationLevel,
    string? SchoolName,
    string? CourseYearGraduated,
    string? SpecifiedProfession,
    string? MobileNumber,
    string? HouseNo,
    string? Street,
    string? Barangay,
    string? CityMunicipality,
    string? Province,
    string? ZipCode,
    string? Country,
    string? MailingHouseNo,
    string? MailingStreet,
    string? MailingBarangay,
    string? MailingCityMunicipality,
    string? MailingProvince,
    string? MailingZipCode,
    string? MailingCountry,
    string? HousePhone,
    string? MembershipNo,
    string? PrcLicenseNo,
    DateOnly? PrcRegistrationDate,
    DateOnly? PrcValidUntilDate,
    string? PtrNumber,
    string? PtrPlaceIssued,
    DateOnly? PtrDateIssued,
    string? Tin,
    bool PrcIdVerified,
    string? PendingPrcLicenseNo,
    DateOnly? PendingPrcRegistrationDate,
    DateOnly? PendingPrcValidUntilDate,
    string? PrcVerificationRejectedReason,
    string Chapter,
    int? ChapterYear,
    string? ChapterPosition,
    string? EmploymentStatus,
    string? Company,
    string? Position,
    string? BusinessAddress,
    int? YearsOfPractice,
    string? Specialization,
    string? Skills,
    string MemberType,
    MembershipStatus Status,
    DateOnly? RenewalDueDate,
    string? NationalDuesReferenceNo,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? SubmittedAt,
    bool IsInGracePeriod,
    /// <summary>Past the renewal date and past the grace period. Derived, not stored - see
    /// MemberService.ComputeIsExpired for why there is no automatic Status transition.</summary>
    bool IsExpired,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Admin-only: creates a Member profile for an existing user. MembershipNo is optional - PSMPE
/// assigns its own control number, and an admin creating a profile may not have it yet. It can be
/// supplied later on the edit form, and is mandatory at approval.
/// </summary>
public record CreateMemberRequest(
    Guid UserId,
    string? MembershipNo,
    string FirstName,
    string? MiddleName,
    string LastName,
    string? Suffix,
    DateOnly? Birthdate,
    string? Gender,
    string? CivilStatus,
    string? EducationLevel,
    string? SchoolName,
    string? CourseYearGraduated,
    string? SpecifiedProfession,
    string? MobileNumber,
    string? HouseNo,
    string? Street,
    string? Barangay,
    string? CityMunicipality,
    string? Province,
    string? ZipCode,
    string? Country,
    string? MailingHouseNo,
    string? MailingStreet,
    string? MailingBarangay,
    string? MailingCityMunicipality,
    string? MailingProvince,
    string? MailingZipCode,
    string? MailingCountry,
    string? HousePhone,
    string? PrcLicenseNo,
    DateOnly? PrcRegistrationDate,
    DateOnly? PrcValidUntilDate,
    string? PtrNumber,
    string? PtrPlaceIssued,
    DateOnly? PtrDateIssued,
    string? Tin,
    string Chapter,
    int? ChapterYear,
    string? ChapterPosition,
    string? EmploymentStatus,
    string? Company,
    string? Position,
    string? BusinessAddress,
    int? YearsOfPractice,
    string? Specialization,
    string? Skills,
    string MemberType,
    DateOnly? RenewalDueDate,
    string? NationalDuesReferenceNo);

/// <summary>
/// Admin-only: can change Status, unlike UpdateMyProfileRequest - membership status is a business
/// decision, not self-service. No PrcIdVerified field here (removed) - verification is only ever
/// set via MemberService.ApprovePrcVerificationAsync/RejectPrcVerificationAsync, so every decision
/// goes through the audit trail rather than a raw toggle.
/// </summary>
public record UpdateMemberRequest(
    string FirstName,
    string? MiddleName,
    string LastName,
    string? Suffix,
    DateOnly? Birthdate,
    string? Gender,
    string? CivilStatus,
    string? EducationLevel,
    string? SchoolName,
    string? CourseYearGraduated,
    string? SpecifiedProfession,
    string? MobileNumber,
    string? HouseNo,
    string? Street,
    string? Barangay,
    string? CityMunicipality,
    string? Province,
    string? ZipCode,
    string? Country,
    string? MailingHouseNo,
    string? MailingStreet,
    string? MailingBarangay,
    string? MailingCityMunicipality,
    string? MailingProvince,
    string? MailingZipCode,
    string? MailingCountry,
    string? HousePhone,
    string? PrcLicenseNo,
    DateOnly? PrcRegistrationDate,
    DateOnly? PrcValidUntilDate,
    string? PtrNumber,
    string? PtrPlaceIssued,
    DateOnly? PtrDateIssued,
    string? Tin,
    string Chapter,
    int? ChapterYear,
    string? ChapterPosition,
    string? EmploymentStatus,
    string? Company,
    string? Position,
    string? BusinessAddress,
    int? YearsOfPractice,
    string? Specialization,
    string? Skills,
    string MemberType,
    MembershipStatus Status,
    DateOnly? RenewalDueDate,
    string? NationalDuesReferenceNo,
    /// <summary>The correction path for a control number mistyped at approval. Null or blank means
    /// "not supplied" and leaves the stored value alone - it is deliberately NOT a way to clear an
    /// approved member's number, and the default keeps every existing caller compiling.</summary>
    string? MembershipNo = null);

/// <summary>
/// Self-service: no Status or MembershipNo - both are admin/business-controlled, not editable by
/// the member themselves. MemberType/Chapter are only honored while the application is still a
/// draft (SubmittedAt null) - see MemberService.UpsertMyProfileAsync. PrcIdReuploaded asserts "I
/// just uploaded a new RMP/PRC ID in this edit" when PrcLicenseNo, PrcRegistrationDate, or
/// PrcValidUntilDate changes - the server independently verifies this against the actual
/// MemberUpload row rather than trusting the flag alone.
/// </summary>
public record UpdateMyProfileRequest(
    string FirstName,
    string? MiddleName,
    string LastName,
    string? Suffix,
    DateOnly? Birthdate,
    string? Gender,
    string? CivilStatus,
    string? EducationLevel,
    string? SchoolName,
    string? CourseYearGraduated,
    string? SpecifiedProfession,
    string? MobileNumber,
    string? HouseNo,
    string? Street,
    string? Barangay,
    string? CityMunicipality,
    string? Province,
    string? ZipCode,
    string? Country,
    string? MailingHouseNo,
    string? MailingStreet,
    string? MailingBarangay,
    string? MailingCityMunicipality,
    string? MailingProvince,
    string? MailingZipCode,
    string? MailingCountry,
    string? HousePhone,
    string? PrcLicenseNo,
    DateOnly? PrcRegistrationDate,
    DateOnly? PrcValidUntilDate,
    string? PtrNumber,
    string? PtrPlaceIssued,
    DateOnly? PtrDateIssued,
    string? Tin,
    string Chapter,
    int? ChapterYear,
    string? ChapterPosition,
    string? EmploymentStatus,
    string? Company,
    string? Position,
    string? BusinessAddress,
    int? YearsOfPractice,
    string? Specialization,
    string? Skills,
    string MemberType,
    bool PrcIdReuploaded = false);

public record RejectPrcVerificationRequest(string Reason);

public record PrcVerificationHistoryDto(
    Guid Id,
    string? OldValue,
    string? NewValue,
    string? DocumentStorageKey,
    PrcVerificationDecision Decision,
    string? Reason,
    Guid DecidedByUserId,
    DateTimeOffset CreatedAt);

/// <summary>
/// Admin-only: approving an application requires PSMPE's own membership control number, which the
/// portal never generates. Free text (trimmed, max 32 chars) so the society's numbering scheme
/// isn't second-guessed, but it must not already belong to another member.
/// </summary>
/// <summary>
/// Admits an application. Approval and payment are now one indivisible act: a member is never
/// admitted without their registration payment being accepted in the same transaction, so nobody
/// ends up approved-but-unpaid.
///
/// <para><paramref name="Payment"/> is only needed when the member has no payment on record - an
/// admin-created profile, or a walk-in paying at the office. A self-service applicant already has
/// one (created at submit), and the admin is reviewing rather than entering it, so this stays null.
/// Supplying it when a payment already exists is rejected rather than silently ignored.</para>
/// </summary>
public record ApproveMemberRequest(string MembershipNo, RecordPaymentRequest? Payment = null);

/// <summary>Payment details entered by an admin on a member's behalf.</summary>
public record RecordPaymentRequest(decimal Amount, string? ReferenceNo, DateOnly PaidOn, string ProofStorageKey);

/// <summary>
/// Advisory answer for the approve dialog's live duplicate check. Echoes the trimmed value back so
/// the caller can discard a response that arrived after the admin typed on (the request is fired
/// per keystroke, debounced, and responses can land out of order).
/// </summary>
public record MembershipNoAvailabilityDto(string MembershipNo, bool IsAvailable);
