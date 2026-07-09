namespace PSMPE.Portal.Domain.Enums;

public static class RoleNames
{
    public const string SuperAdmin = "Super Admin";
    public const string Admin = "Admin";
    public const string ContentCreator = "Content Creator";

    public static readonly string[] All = [SuperAdmin, Admin, ContentCreator];
}
