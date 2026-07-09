using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Content;
using PSMPE.Portal.Application.Content.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Infrastructure.Authorization.Policies;

namespace PSMPE.Portal.WebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/content")]
public class ContentController(IContentService contentService, IAuthorizationService authorizationService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContentItemDto>>> GetAll(CancellationToken cancellationToken)
        => Ok(await contentService.GetAllAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContentItemDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await contentService.GetByIdAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<ContentItemDto>> Create(CreateContentItemRequest request, CancellationToken cancellationToken)
    {
        var created = await contentService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateContentItemRequest request, CancellationToken cancellationToken)
    {
        var existing = await contentService.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        // Resource-based check via the ownership policy, in addition to ContentService's own
        // ownership enforcement — demonstrates ASP.NET Core's resource-based authorization API.
        var authResult = await authorizationService.AuthorizeAsync(
            User, new ContentItem { Id = existing.Id, OwnerId = existing.OwnerId }, PolicyNames.ContentOwnerOrAdmin);
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        var result = await contentService.UpdateAsync(id, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var existing = await contentService.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var authResult = await authorizationService.AuthorizeAsync(
            User, new ContentItem { Id = existing.Id, OwnerId = existing.OwnerId }, PolicyNames.ContentOwnerOrAdmin);
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        var result = await contentService.DeleteAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    private IActionResult ToActionResult(Result result)
    {
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
