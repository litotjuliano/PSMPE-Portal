using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Caching;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Application.Locations;

public class LocationService(IApplicationDbContext db, ICacheService? cache = null) : ILocationService
{
    // Optional so tests constructing the service directly get no-op caching (same as LayoutService).
    private ICacheService Cache => cache ?? NoOpCacheService.Instance;
    private const string AllCacheKey = "locations:all";

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public Task<IReadOnlyList<LocationRowDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Cache.GetOrCreateAsync(AllCacheKey, "Cache:LocationDurationSeconds", 300, async () =>
        {
            var rows = await db.PhCities.AsNoTracking()
                .OrderBy(c => c.Province!.Region!.SortOrder)
                .ThenBy(c => c.Province!.Name)
                .ThenBy(c => c.Name)
                .Select(c => new LocationRowDto(c.Province!.Region!.Name, c.Province.Name, c.Name, c.ZipCode))
                .ToListAsync(cancellationToken);
            return (IReadOnlyList<LocationRowDto>)rows;
        });

    // ---- Regions --------------------------------------------------------------------------

    public async Task<IReadOnlyList<RegionDto>> GetRegionsAsync(string? search, CancellationToken cancellationToken = default)
    {
        var query = db.PhRegions.AsNoTracking();
        if (Clean(search) is { } term)
        {
            var lowered = term.ToLower();
            query = query.Where(r => r.Name.ToLower().Contains(lowered));
        }

        return await query.OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
            .Select(r => new RegionDto(r.Id, r.Name, r.SortOrder, r.Provinces.Count))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<RegionDto>> CreateRegionAsync(SaveRegionRequest request, CancellationToken cancellationToken = default)
    {
        var name = Clean(request.Name);
        if (name is null) return Result<RegionDto>.Failure("Region name is required.");
        if (await db.PhRegions.AnyAsync(r => r.Name.ToLower() == name.ToLower(), cancellationToken))
            return Result<RegionDto>.Conflict($"Region '{name}' already exists.");

        var sortOrder = request.SortOrder
            ?? (await db.PhRegions.AnyAsync(cancellationToken) ? await db.PhRegions.MaxAsync(r => r.SortOrder, cancellationToken) + 1 : 1);
        var region = new PhRegion { Name = name, SortOrder = sortOrder };
        db.PhRegions.Add(region);
        await db.SaveChangesAsync(cancellationToken);
        Cache.Remove(AllCacheKey);
        return Result<RegionDto>.Success(new RegionDto(region.Id, region.Name, region.SortOrder, 0));
    }

    public async Task<Result<RegionDto>> UpdateRegionAsync(Guid id, SaveRegionRequest request, CancellationToken cancellationToken = default)
    {
        var region = await db.PhRegions.Include(r => r.Provinces).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (region is null) return Result<RegionDto>.NotFound($"Region '{id}' was not found.");
        var name = Clean(request.Name);
        if (name is null) return Result<RegionDto>.Failure("Region name is required.");
        if (await db.PhRegions.AnyAsync(r => r.Id != id && r.Name.ToLower() == name.ToLower(), cancellationToken))
            return Result<RegionDto>.Conflict($"Region '{name}' already exists.");

        region.Name = name;
        region.SortOrder = request.SortOrder ?? region.SortOrder;
        region.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        Cache.Remove(AllCacheKey);
        return Result<RegionDto>.Success(new RegionDto(region.Id, region.Name, region.SortOrder, region.Provinces.Count));
    }

    public async Task<Result> DeleteRegionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var region = await db.PhRegions.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (region is null) return Result.NotFound($"Region '{id}' was not found.");
        var provinces = await db.PhProvinces.CountAsync(p => p.RegionId == id, cancellationToken);
        if (provinces > 0)
            return Result.Conflict($"'{region.Name}' still has {provinces} province(s). Delete or move them first.");

        db.PhRegions.Remove(region);
        await db.SaveChangesAsync(cancellationToken);
        Cache.Remove(AllCacheKey);
        return Result.Success();
    }

    // ---- Provinces ------------------------------------------------------------------------

    public async Task<PagedResult<ProvinceDto>> GetProvincesAsync(
        int page, int pageSize, string? search, Guid? regionId, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.PhProvinces.AsNoTracking();
        if (regionId is { } rid) query = query.Where(p => p.RegionId == rid);
        if (Clean(search) is { } term)
        {
            var lowered = term.ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(lowered));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(p => p.Region!.SortOrder).ThenBy(p => p.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new ProvinceDto(p.Id, p.Name, p.RegionId, p.Region!.Name, p.Cities.Count))
            .ToListAsync(cancellationToken);
        return new PagedResult<ProvinceDto>(items, total, page, pageSize);
    }

    public async Task<Result<ProvinceDto>> CreateProvinceAsync(SaveProvinceRequest request, CancellationToken cancellationToken = default)
    {
        var name = Clean(request.Name);
        if (name is null) return Result<ProvinceDto>.Failure("Province name is required.");
        var region = await db.PhRegions.FirstOrDefaultAsync(r => r.Id == request.RegionId, cancellationToken);
        if (region is null) return Result<ProvinceDto>.Failure("Choose a valid region.");
        if (await db.PhProvinces.AnyAsync(p => p.RegionId == region.Id && p.Name.ToLower() == name.ToLower(), cancellationToken))
            return Result<ProvinceDto>.Conflict($"'{name}' already exists in {region.Name}.");

        var province = new PhProvince { Name = name, RegionId = region.Id };
        db.PhProvinces.Add(province);
        await db.SaveChangesAsync(cancellationToken);
        Cache.Remove(AllCacheKey);
        return Result<ProvinceDto>.Success(new ProvinceDto(province.Id, province.Name, region.Id, region.Name, 0));
    }

    public async Task<Result<ProvinceDto>> UpdateProvinceAsync(Guid id, SaveProvinceRequest request, CancellationToken cancellationToken = default)
    {
        var province = await db.PhProvinces.Include(p => p.Cities).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (province is null) return Result<ProvinceDto>.NotFound($"Province '{id}' was not found.");
        var name = Clean(request.Name);
        if (name is null) return Result<ProvinceDto>.Failure("Province name is required.");
        var region = await db.PhRegions.FirstOrDefaultAsync(r => r.Id == request.RegionId, cancellationToken);
        if (region is null) return Result<ProvinceDto>.Failure("Choose a valid region.");
        if (await db.PhProvinces.AnyAsync(p => p.Id != id && p.RegionId == region.Id && p.Name.ToLower() == name.ToLower(), cancellationToken))
            return Result<ProvinceDto>.Conflict($"'{name}' already exists in {region.Name}.");

        province.Name = name;
        province.RegionId = region.Id;
        province.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        Cache.Remove(AllCacheKey);
        return Result<ProvinceDto>.Success(new ProvinceDto(province.Id, province.Name, region.Id, region.Name, province.Cities.Count));
    }

    public async Task<Result> DeleteProvinceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var province = await db.PhProvinces.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (province is null) return Result.NotFound($"Province '{id}' was not found.");
        var cities = await db.PhCities.CountAsync(c => c.ProvinceId == id, cancellationToken);
        if (cities > 0)
            return Result.Conflict($"'{province.Name}' still has {cities} city/municipality entries. Delete or move them first.");

        db.PhProvinces.Remove(province);
        await db.SaveChangesAsync(cancellationToken);
        Cache.Remove(AllCacheKey);
        return Result.Success();
    }

    // ---- Cities ---------------------------------------------------------------------------

    public async Task<PagedResult<CityDto>> GetCitiesAsync(
        int page, int pageSize, string? search, Guid? regionId, Guid? provinceId, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.PhCities.AsNoTracking();
        if (provinceId is { } pid) query = query.Where(c => c.ProvinceId == pid);
        else if (regionId is { } rid) query = query.Where(c => c.Province!.RegionId == rid);
        if (Clean(search) is { } term)
        {
            var lowered = term.ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(lowered)
                || (c.ZipCode != null && c.ZipCode.Contains(lowered)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(c => c.Province!.Region!.SortOrder).ThenBy(c => c.Province!.Name).ThenBy(c => c.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new CityDto(c.Id, c.Name, c.ZipCode, c.ProvinceId, c.Province!.Name, c.Province.RegionId, c.Province.Region!.Name))
            .ToListAsync(cancellationToken);
        return new PagedResult<CityDto>(items, total, page, pageSize);
    }

    public async Task<Result<CityDto>> CreateCityAsync(SaveCityRequest request, CancellationToken cancellationToken = default)
    {
        var name = Clean(request.Name);
        if (name is null) return Result<CityDto>.Failure("City or municipality name is required.");
        var province = await db.PhProvinces.Include(p => p.Region).FirstOrDefaultAsync(p => p.Id == request.ProvinceId, cancellationToken);
        if (province is null) return Result<CityDto>.Failure("Choose a valid province.");
        if (await db.PhCities.AnyAsync(c => c.ProvinceId == province.Id && c.Name.ToLower() == name.ToLower(), cancellationToken))
            return Result<CityDto>.Conflict($"'{name}' already exists in {province.Name}.");

        var city = new PhCity { Name = name, ZipCode = Clean(request.ZipCode), ProvinceId = province.Id };
        db.PhCities.Add(city);
        await db.SaveChangesAsync(cancellationToken);
        Cache.Remove(AllCacheKey);
        return Result<CityDto>.Success(ToDto(city, province));
    }

    public async Task<Result<CityDto>> UpdateCityAsync(Guid id, SaveCityRequest request, CancellationToken cancellationToken = default)
    {
        var city = await db.PhCities.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (city is null) return Result<CityDto>.NotFound($"City '{id}' was not found.");
        var name = Clean(request.Name);
        if (name is null) return Result<CityDto>.Failure("City or municipality name is required.");
        var province = await db.PhProvinces.Include(p => p.Region).FirstOrDefaultAsync(p => p.Id == request.ProvinceId, cancellationToken);
        if (province is null) return Result<CityDto>.Failure("Choose a valid province.");
        if (await db.PhCities.AnyAsync(c => c.Id != id && c.ProvinceId == province.Id && c.Name.ToLower() == name.ToLower(), cancellationToken))
            return Result<CityDto>.Conflict($"'{name}' already exists in {province.Name}.");

        city.Name = name;
        city.ZipCode = Clean(request.ZipCode);
        city.ProvinceId = province.Id;
        city.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        Cache.Remove(AllCacheKey);
        return Result<CityDto>.Success(ToDto(city, province));
    }

    public async Task<Result> DeleteCityAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var city = await db.PhCities.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (city is null) return Result.NotFound($"City '{id}' was not found.");

        db.PhCities.Remove(city);
        await db.SaveChangesAsync(cancellationToken);
        Cache.Remove(AllCacheKey);
        return Result.Success();
    }

    private static CityDto ToDto(PhCity city, PhProvince province) =>
        new(city.Id, city.Name, city.ZipCode, province.Id, province.Name, province.RegionId, province.Region!.Name);
}
