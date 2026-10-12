using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PSMPE.Portal.Application.Locations;
using PSMPE.Portal.Infrastructure.Authorization;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>
/// Read side of the Philippine location reference data, for the address picker. Any signed-in user
/// may read it; editing lives in <see cref="AdminLocationsController"/>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/locations")]
public class LocationsController(ILocationService locationService) : ControllerBase
{
    // Reference data a not-yet-approved applicant needs to fill in the membership form, so it must
    // stay readable past MembershipAccessMiddleware (applicants have no Member row / portal access yet).
    [HttpGet]
    [AllowExpiredMember]
    public async Task<ActionResult<IReadOnlyList<LocationRowDto>>> GetAll(CancellationToken cancellationToken)
        => Ok(await locationService.GetAllAsync(cancellationToken));
}
