using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PSMPE.Portal.Application.Chapters;
using PSMPE.Portal.Infrastructure.Authorization;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>
/// Read side of the chapter list, for dropdowns and filters. Any signed-in user may read it;
/// editing lives in <see cref="AdminChaptersController"/>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/chapters")]
public class ChaptersController(IChapterService chapterService) : ControllerBase
{
    // Needed by the membership application form, so - like the location data - it must stay
    // readable past MembershipAccessMiddleware for applicants who aren't members yet. Inactive
    // chapters are included (flagged) so a screen showing a member's saved chapter can still name it;
    // pickers filter to IsActive themselves.
    [HttpGet]
    [AllowExpiredMember]
    public async Task<ActionResult<IReadOnlyList<ChapterDto>>> GetAll(CancellationToken cancellationToken)
        => Ok(await chapterService.GetAllAsync(cancellationToken: cancellationToken));
}
