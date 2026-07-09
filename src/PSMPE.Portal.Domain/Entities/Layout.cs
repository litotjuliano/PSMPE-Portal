namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// A reusable page layout. System layouts (IsSystemLayout = true, OwnerId = null) ship with the
/// platform and can only ever be deleted by a Super Admin — see SystemAdminAuthorizationHandler.
/// </summary>
public class Layout : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
    public bool IsSystemLayout { get; set; }

    public Guid? OwnerId { get; set; }
    public ApplicationUser? Owner { get; set; }
}
