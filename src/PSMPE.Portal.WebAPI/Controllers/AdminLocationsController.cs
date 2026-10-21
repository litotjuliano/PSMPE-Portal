using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Locations;
using PSMPE.Portal.Infrastructure.Authorization.Policies;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>Super Admin maintenance of regions, provinces and cities/municipalities.</summary>
[ApiController]
[Route("api/admin/locations")]
[Authorize(Policy = PolicyNames.RequireSuperAdmin)]
public class AdminLocationsController(ILocationService locationService) : ControllerBase
{
    // ---- Regions ----

    [HttpGet("regions")]
    public async Task<ActionResult<IReadOnlyList<RegionDto>>> GetRegions(string? search, CancellationToken cancellationToken)
        => Ok(await locationService.GetRegionsAsync(search, cancellationToken));

    [HttpPost("regions")]
    public async Task<IActionResult> CreateRegion(SaveRegionRequest request, CancellationToken cancellationToken)
        => ToResponse(await locationService.CreateRegionAsync(request, cancellationToken));

    [HttpPut("regions/{id:guid}")]
    public async Task<IActionResult> UpdateRegion(Guid id, SaveRegionRequest request, CancellationToken cancellationToken)
        => ToResponse(await locationService.UpdateRegionAsync(id, request, cancellationToken));

    [HttpDelete("regions/{id:guid}")]
    public async Task<IActionResult> DeleteRegion(Guid id, CancellationToken cancellationToken)
        => ToResponse(await locationService.DeleteRegionAsync(id, cancellationToken));

    // ---- Provinces ----

    [HttpGet("provinces")]
    public async Task<ActionResult<PagedResult<ProvinceDto>>> GetProvinces(
        int page = 1, int pageSize = 20, string? search = null, Guid? regionId = null, CancellationToken cancellationToken = default)
        => Ok(await locationService.GetProvincesAsync(page, pageSize, search, regionId, cancellationToken));

    [HttpPost("provinces")]
    public async Task<IActionResult> CreateProvince(SaveProvinceRequest request, CancellationToken cancellationToken)
        => ToResponse(await locationService.CreateProvinceAsync(request, cancellationToken));

    [HttpPut("provinces/{id:guid}")]
    public async Task<IActionResult> UpdateProvince(Guid id, SaveProvinceRequest request, CancellationToken cancellationToken)
        => ToResponse(await locationService.UpdateProvinceAsync(id, request, cancellationToken));

    [HttpDelete("provinces/{id:guid}")]
    public async Task<IActionResult> DeleteProvince(Guid id, CancellationToken cancellationToken)
        => ToResponse(await locationService.DeleteProvinceAsync(id, cancellationToken));

    // ---- Cities / municipalities ----

    [HttpGet("cities")]
    public async Task<ActionResult<PagedResult<CityDto>>> GetCities(
        int page = 1, int pageSize = 20, string? search = null, Guid? regionId = null, Guid? provinceId = null,
        CancellationToken cancellationToken = default)
        => Ok(await locationService.GetCitiesAsync(page, pageSize, search, regionId, provinceId, cancellationToken));

    [HttpPost("cities")]
    public async Task<IActionResult> CreateCity(SaveCityRequest request, CancellationToken cancellationToken)
        => ToResponse(await locationService.CreateCityAsync(request, cancellationToken));

    [HttpPut("cities/{id:guid}")]
    public async Task<IActionResult> UpdateCity(Guid id, SaveCityRequest request, CancellationToken cancellationToken)
        => ToResponse(await locationService.UpdateCityAsync(id, request, cancellationToken));

    [HttpDelete("cities/{id:guid}")]
    public async Task<IActionResult> DeleteCity(Guid id, CancellationToken cancellationToken)
        => ToResponse(await locationService.DeleteCityAsync(id, cancellationToken));

    private IActionResult ToResponse<T>(Result<T> result) =>
        result.Succeeded ? Ok(result.Value) : ToError(result);

    private IActionResult ToResponse(Result result) =>
        result.Succeeded ? NoContent() : ToError(result);

    private IActionResult ToError(Result result) => result.ErrorType switch
    {
        ResultErrorType.NotFound => NotFound(new { message = result.Error }),
        ResultErrorType.Conflict => Conflict(new { message = result.Error }),
        ResultErrorType.Forbidden => Forbid(),
        _ => BadRequest(new { message = result.Error })
    };
}
