namespace PSMPE.Portal.Domain.Entities;

/// <summary>Simple key/value store for seeded system-wide configuration.</summary>
public class SystemConfig : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
}
