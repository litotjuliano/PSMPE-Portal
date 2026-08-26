using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>Testing-only - lets integration tests exercise ExceptionHandlingMiddleware's real
/// unhandled-exception path without depending on some other endpoint happening to be breakable.
/// 404s outside the Testing environment.</summary>
[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController(IWebHostEnvironment env) : ControllerBase
{
    [HttpGet("throw")]
    public IActionResult Throw()
    {
        if (!env.IsEnvironment("Testing"))
        {
            return NotFound();
        }

        throw new InvalidOperationException("Deliberate test exception");
    }
}
