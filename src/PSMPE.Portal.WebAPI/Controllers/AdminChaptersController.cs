using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PSMPE.Portal.Application.Chapters;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Infrastructure.Authorization.Policies;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>Super Admin maintenance of chapters (add, rename, reorder, deactivate, delete).</summary>
[ApiController]
[Route("api/admin/chapters")]
[Authorize(Policy = PolicyNames.RequireSuperAdmin)]
public class AdminChaptersController(IChapterService chapterService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChapterDto>>> GetAll(
        string? search = null, bool? isActive = null, CancellationToken cancellationToken = default)
        => Ok(await chapterService.GetAllAsync(search, isActive, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(SaveChapterRequest request, CancellationToken cancellationToken)
        => ToResponse(await chapterService.CreateAsync(request, cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveChapterRequest request, CancellationToken cancellationToken)
        => ToResponse(await chapterService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        => ToResponse(await chapterService.DeleteAsync(id, cancellationToken));

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
