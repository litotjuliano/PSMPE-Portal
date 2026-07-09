using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Layouts;
using PSMPE.Portal.Application.Layouts.Dtos;

namespace PSMPE.Portal.WebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/layouts")]
public class LayoutsController(ILayoutService layoutService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LayoutDto>>> GetAll(CancellationToken cancellationToken)
        => Ok(await layoutService.GetAllAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<LayoutDto>> Create(CreateLayoutRequest request, CancellationToken cancellationToken)
        => Ok(await layoutService.CreateAsync(request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        // Ownership + "no deleting system layouts" rules are enforced inside LayoutService.
        var result = await layoutService.DeleteAsync(id, cancellationToken);
        if (result.Succeeded)
        {
            return NoContent();
        }

        return result.ErrorType switch
        {
            ResultErrorType.NotFound => NotFound(new { message = result.Error }),
            ResultErrorType.Forbidden => Forbid(),
            _ => BadRequest(new { message = result.Error })
        };
    }
}
