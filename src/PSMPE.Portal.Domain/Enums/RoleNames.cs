namespace PSMPE.Portal.Domain.Enums;

public static class RoleNames
{
    public const string SuperAdmin = "Super Admin";
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Accounts = "Accounts";
    public const string Approval = "Approval";
    public const string Member = "Member";

    public static readonly string[] All = [SuperAdmin, Admin, Manager, Accounts, Approval, Member];

    /// <summary>
    /// True for a staff-only account: it holds an administrative role and NOT the Member role.
    /// Such accounts have no membership profile and never appear in the member list. An account
    /// that holds Member as well (e.g. Admin + Member) is a real member who also has admin
    /// access: it applies, pays and is approved like anyone else.
    /// </summary>
    public static bool IsStaffOnly(IEnumerable<string> roles)
    {
        var held = roles as IReadOnlyCollection<string> ?? roles.ToList();
        return held.Any(r => r != Member) && !held.Contains(Member);
    }
}
