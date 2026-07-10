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
    string Chapter,
    string? Company,
    MembershipStatus Status,
    DateOnly? RenewalDueDate,
    string? NationalDuesReferenceNo,
    string? PhotoUrl,
    string? PrcIdUrl,
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
    DateOnly? RenewalDueDate,
    string? NationalDuesReferenceNo);

/// <summary>Admin-only: can change Status, unlike UpdateMyProfileRequest - membership status is a business decision, not self-service.</summary>
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
    MembershipStatus Status,
    DateOnly? RenewalDueDate,
    string? NationalDuesReferenceNo);

/// <summary>Self-service: no Status or MembershipNo - both are admin/business-controlled, not editable by the member themselves.</summary>
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
    string? Company);
