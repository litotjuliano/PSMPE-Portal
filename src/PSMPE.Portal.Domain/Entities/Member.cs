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
    public string? Address { get; set; }

    public string MembershipNo { get; set; } = string.Empty;
    public string? PrcLicenseNo { get; set; }
    public string Chapter { get; set; } = string.Empty;
    public string? Company { get; set; }

    public MembershipStatus Status { get; set; } = MembershipStatus.Pending;
    public DateOnly? RenewalDueDate { get; set; }
    public string? NationalDuesReferenceNo { get; set; }

    public string? PhotoUrl { get; set; }
    public string? PrcIdUrl { get; set; }
}
