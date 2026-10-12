using PSMPE.Portal.Application.Common.Models;

namespace PSMPE.Portal.Application.Locations;

public interface ILocationService
{
    Task<IReadOnlyList<LocationRowDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RegionDto>> GetRegionsAsync(string? search, CancellationToken cancellationToken = default);
    Task<Result<RegionDto>> CreateRegionAsync(SaveRegionRequest request, CancellationToken cancellationToken = default);
    Task<Result<RegionDto>> UpdateRegionAsync(Guid id, SaveRegionRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteRegionAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<ProvinceDto>> GetProvincesAsync(int page, int pageSize, string? search, Guid? regionId, CancellationToken cancellationToken = default);
    Task<Result<ProvinceDto>> CreateProvinceAsync(SaveProvinceRequest request, CancellationToken cancellationToken = default);
    Task<Result<ProvinceDto>> UpdateProvinceAsync(Guid id, SaveProvinceRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteProvinceAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<CityDto>> GetCitiesAsync(int page, int pageSize, string? search, Guid? regionId, Guid? provinceId, CancellationToken cancellationToken = default);
    Task<Result<CityDto>> CreateCityAsync(SaveCityRequest request, CancellationToken cancellationToken = default);
    Task<Result<CityDto>> UpdateCityAsync(Guid id, SaveCityRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteCityAsync(Guid id, CancellationToken cancellationToken = default);
}
