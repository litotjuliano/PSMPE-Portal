namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// A PSMPE chapter (NCR, Cebu, ...), maintained by a Super Admin. Members and Events point at a row
/// by ChapterId, so renaming one updates everywhere; a chapter still referenced can't be deleted,
/// only deactivated (IsActive = false hides it from new-selection dropdowns while existing members
/// and events keep it).
/// </summary>
public class Chapter : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Keeps the dashboard chart and dropdowns in a deliberate order rather than alphabetical.</summary>
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
