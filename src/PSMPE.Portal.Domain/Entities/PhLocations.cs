namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// Philippine address reference data (Region > Province > City/Municipality), maintained by a Super
/// Admin. Member addresses store plain text, not foreign keys to these rows, so editing or deleting
/// one only changes what the address picker offers.
/// </summary>
public class PhRegion : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Keeps "Region I ... NCR" in its natural order; alphabetical would scramble it.</summary>
    public int SortOrder { get; set; }
    public List<PhProvince> Provinces { get; set; } = [];
}

public class PhProvince : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid RegionId { get; set; }
    public PhRegion? Region { get; set; }
    public List<PhCity> Cities { get; set; } = [];
}

public class PhCity : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Null for entries with no well-documented ZIP; the UI leaves ZIP editable.</summary>
    public string? ZipCode { get; set; }
    public Guid ProvinceId { get; set; }
    public PhProvince? Province { get; set; }
}
