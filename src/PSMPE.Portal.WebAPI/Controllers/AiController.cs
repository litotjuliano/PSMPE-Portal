using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PSMPE.Portal.Application.AI;
using PSMPE.Portal.Application.AI.Dtos;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Authorization;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>
/// Starter endpoint structure for future prompt execution from the frontend.
/// TODO: add per-user rate limiting and usage tracking before exposing this beyond internal testing.
/// </summary>
[ApiController]
[Authorize]
[Route("api/ai")]
public class AiController(IPromptExecutionService promptExecutionService) : ControllerBase
{
    [HttpPost("prompt")]
    [RequirePermission(Permissions.Ai.UsePrompt)]
    public async Task<ActionResult<PromptResponseDto>> ExecutePrompt(PromptRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest(new { message = "Prompt must not be empty." });
        }

        var response = await promptExecutionService.ExecuteAsync(request, cancellationToken);
        return Ok(response);
    }
}
