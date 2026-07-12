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
    string? Address,
    string MembershipNo,
    string? PrcLicenseNo,
    bool PrcIdVerified,
    string? PendingPrcLicenseNo,
    string? PrcVerificationRejectedReason,
    string Chapter,
    string? Company,
    string MemberType,
    MembershipStatus Status,
    DateOnly? RenewalDueDate,
    string? NationalDuesReferenceNo,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? SubmittedAt,
    bool IsInGracePeriod,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Admin-only: creates a Member profile for an existing user, with an explicitly assigned MembershipNo.</summary>
public record CreateMemberRequest(
    Guid UserId,
    string MembershipNo,
    string FirstName,
    string? MiddleName,
    string LastName,
    string? Suffix,
    DateOnly? Birthdate,
    string? Gender,
    string? Address,
    string? PrcLicenseNo,
    string Chapter,
    string? Company,
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
    string? Address,
    string? PrcLicenseNo,
    string Chapter,
    string? Company,
    string MemberType,
    MembershipStatus Status,
    DateOnly? RenewalDueDate,
    string? NationalDuesReferenceNo);

/// <summary>
/// Self-service: no Status or MembershipNo - both are admin/business-controlled, not editable by
/// the member themselves. MemberType/Chapter are only honored while the application is still a
/// draft (SubmittedAt null) - see MemberService.UpsertMyProfileAsync. PrcIdReuploaded asserts "I
/// just uploaded a new PRC ID in this edit" when PrcLicenseNo changes - the server independently
/// verifies this against the actual MemberUpload row rather than trusting the flag alone.
/// </summary>
public record UpdateMyProfileRequest(
    string FirstName,
    string? MiddleName,
    string LastName,
    string? Suffix,
    DateOnly? Birthdate,
    string? Gender,
    string? Address,
    string? PrcLicenseNo,
    string Chapter,
    string? Company,
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
