namespace PSMPE.Portal.Application.Locations;

/// <summary>One flat row per city, the shape the address picker indexes.</summary>
public record LocationRowDto(string Region, string Province, string City, string? ZipCode);

public record RegionDto(Guid Id, string Name, int SortOrder, int ProvinceCount);

public record ProvinceDto(Guid Id, string Name, Guid RegionId, string RegionName, int CityCount);

public record CityDto(Guid Id, string Name, string? ZipCode, Guid ProvinceId, string ProvinceName, Guid RegionId, string RegionName);

/// <summary>SortOrder null on create places the region last.</summary>
public record SaveRegionRequest(string Name, int? SortOrder);

public record SaveProvinceRequest(string Name, Guid RegionId);

public record SaveCityRequest(string Name, string? ZipCode, Guid ProvinceId);
