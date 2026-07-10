namespace PSMPE.Portal.Domain.Enums;

public static class Permissions
{
    /// <summary>Claim type used to store/read permission grants on Identity roles and in the JWT.</summary>
    public const string ClaimType = "permission";

    public static class Content
    {
        public const string Create = "content:create";
        public const string Update = "content:update";
        public const string Delete = "content:delete";
        public const string ManageOthers = "content:manage-others";
    }

    public static class Layout
    {
        public const string Create = "layout:create";
        public const string Delete = "layout:delete";
        public const string DeleteSystem = "layout:delete-system";
    }

    public static class Admin
    {
        public const string ManageUsers = "admin:manage-users";
        public const string ManageRoles = "admin:manage-roles";
    }

    public static class Ai
    {
        public const string UsePrompt = "ai:use-prompt";
    }

    public static class Members
    {
        public const string View = "members:view";
        public const string Manage = "members:manage";
    }

    public static readonly string[] All =
    [
        Content.Create, Content.Update, Content.Delete, Content.ManageOthers,
        Layout.Create, Layout.Delete, Layout.DeleteSystem,
        Admin.ManageUsers, Admin.ManageRoles,
        Ai.UsePrompt,
        Members.View, Members.Manage
    ];
}
