using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// A piece of CMS content owned by a user. Ownership drives who may edit/delete it —
/// see OwnershipAuthorizationHandler in Infrastructure.
/// </summary>
public class ContentItem : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    public Guid OwnerId { get; set; }
    public ApplicationUser? Owner { get; set; }

    public Guid? LayoutId { get; set; }
    public Layout? Layout { get; set; }
}
