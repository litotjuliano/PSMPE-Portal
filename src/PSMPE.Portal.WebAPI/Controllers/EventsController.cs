using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Events;
using PSMPE.Portal.Application.Events.Dtos;
using PSMPE.Portal.Application.Payments;
using PSMPE.Portal.Application.Payments.Dtos;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Authorization;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>
/// Event Management and CPD Credit Tracking - see openspecs/events.md (a later task) and
/// add-events-cpd-tracker/proposal.md for the full design. Payment verification/rejection for an
/// event registration's Payment happens through the existing PaymentsController endpoints
/// unchanged - only PaymentService's internals branch on Kind. This controller only owns the two
/// payment actions that are genuinely new: member proof submission and admin cash recording, both
/// scoped to a registration id rather than a bare payment id.
/// </summary>
[ApiController]
[Authorize]
[Route("api/events")]
public class EventsController(IEventService eventService, IPaymentService paymentService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<EventDto>>> GetAll(
        int page = 1, int pageSize = 20, string? search = null, string? chapter = null, bool upcomingOnly = false,
        CancellationToken cancellationToken = default) =>
        Ok(await eventService.GetAllAsync(page, pageSize, search, chapter, upcomingOnly, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EventDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var @event = await eventService.GetByIdAsync(id, cancellationToken);
        return @event is null ? NotFound() : Ok(@event);
    }

    [HttpPost]
    [RequirePermission(Permissions.Events.Manage)]
    public async Task<ActionResult<EventDto>> Create(CreateEventRequest request, CancellationToken cancellationToken)
    {
        var result = await eventService.CreateAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Events.Manage)]
    public async Task<ActionResult<EventDto>> Update(Guid id, UpdateEventRequest request, CancellationToken cancellationToken)
    {
        var result = await eventService.UpdateAsync(id, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToErrorActionResult(result);
    }

    [HttpPost("{id:guid}/register")]
    public async Task<ActionResult<EventRegistrationDto>> Register(Guid id, RegisterForEventRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await eventService.RegisterAsync(userId.Value, id, request.Mode, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToErrorActionResult(result);
    }

    [HttpPost("registrations/{id:guid}/cancel")]
    public async Task<IActionResult> CancelRegistration(Guid id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        return ToActionResult(await eventService.CancelRegistrationAsync(userId.Value, id, cancellationToken));
    }

    [HttpPost("registrations/{id:guid}/payment")]
    public async Task<ActionResult<PaymentDto>> SubmitPayment(Guid id, SubmitPaymentRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await paymentService.SubmitForEventRegistrationAsync(userId.Value, id, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToErrorActionResult(result);
    }

    /// <summary>Admin-only. Reaches PaymentVerified in one call, with no proof file - see
    /// PaymentService.RecordEventCashPaymentAsync.</summary>
    [HttpPost("registrations/{id:guid}/payment/cash")]
    [RequirePermission(Permissions.Events.Manage)]
    public async Task<ActionResult<PaymentDto>> RecordCashPayment(Guid id, RecordCashPaymentRequest request, CancellationToken cancellationToken)
    {
        var decidedBy = CurrentUserId;
        if (decidedBy is null)
        {
            return Unauthorized();
        }

        var result = await paymentService.RecordEventCashPaymentAsync(id, request.Amount, decidedBy.Value, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToErrorActionResult(result);
    }

    /// <summary>Bulk roster reconciliation - one call covers every registrant an admin has worked
    /// through on the printed sign-in sheet, not just one. See EventService.RecordAttendanceAsync.</summary>
    [HttpPost("{id:guid}/roster/attendance")]
    [RequirePermission(Permissions.Events.Manage)]
    public async Task<IActionResult> RecordAttendance(Guid id, RecordAttendanceRequest request, CancellationToken cancellationToken)
    {
        var adminUserId = CurrentUserId;
        if (adminUserId is null)
        {
            return Unauthorized();
        }

        return ToActionResult(await eventService.RecordAttendanceAsync(id, request.Registrants, adminUserId.Value, cancellationToken));
    }

    [HttpPost("registrations/{id:guid}/evaluation")]
    public async Task<IActionResult> SubmitEvaluation(Guid id, SubmitEvaluationRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        return ToActionResult(await eventService.SubmitEvaluationAsync(userId.Value, id, request.Rating, request.Comments, cancellationToken));
    }

    [HttpGet("{id:guid}/roster")]
    [RequirePermission(Permissions.Events.View, Permissions.Events.Manage)]
    public async Task<ActionResult<EventRosterDto>> GetRoster(Guid id, CancellationToken cancellationToken)
    {
        var result = await eventService.GetRosterAsync(id, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToErrorActionResult(result);
    }

    /// <summary>Streams the PDF directly - never stored, generated fresh on every request (see
    /// CertificatePdfGenerator). Members may only fetch their own; staff need events:view or
    /// events:manage. isAdmin here is derived from the authenticated user's own permission claims
    /// (User.HasClaim), never from any client-supplied request value - this is the ownership-bypass
    /// flag EventService.GetCertificateDataAsync documents, and it must only ever reflect what the
    /// server itself knows about the caller. Reachable even while Expired, same as
    /// MembersController.GetMyCpd - a member should still be able to retrieve proof of CPD credit
    /// they already earned even after their membership has lapsed.</summary>
    [HttpGet("registrations/{id:guid}/certificate")]
    [AllowExpiredMember]
    public async Task<IActionResult> GetCertificate(Guid id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var isAdmin = User.HasClaim(Permissions.ClaimType, Permissions.Events.View) ||
                      User.HasClaim(Permissions.ClaimType, Permissions.Events.Manage);
        var result = await eventService.GetCertificateDataAsync(userId.Value, id, isAdmin, cancellationToken);
        if (!result.Succeeded)
        {
            return ToErrorActionResult(result);
        }

        var pdfBytes = CertificatePdfGenerator.Generate(result.Value!);
        return File(pdfBytes, "application/pdf", $"{result.Value!.EventTitle}-certificate.pdf");
    }

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    private IActionResult ToActionResult(Result result)
    {
        if (result.Succeeded)
        {
            return NoContent();
        }

        return ToErrorActionResult(result);
    }

    private ActionResult ToErrorActionResult(Result result) => result.ErrorType switch
    {
        ResultErrorType.NotFound => NotFound(new { message = result.Error }),
        ResultErrorType.Forbidden => Forbid(),
        ResultErrorType.Conflict => Conflict(new { message = result.Error }),
        _ => BadRequest(new { message = result.Error }),
    };
}
