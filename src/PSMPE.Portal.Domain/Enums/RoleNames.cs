namespace PSMPE.Portal.Domain.Enums;

public static class RoleNames
{
    public const string SuperAdmin = "Super Admin";
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Accounts = "Accounts";
    public const string Member = "Member";

    public static readonly string[] All = [SuperAdmin, Admin, Manager, Accounts, Member];
}
