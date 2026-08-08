using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Application.Payments;
using PSMPE.Portal.Application.Payments.Dtos;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Authorization;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>
/// Membership payments - the initial fee and annual renewals. Members submit; admins verify.
/// Verifying is the only thing that activates a membership or moves a renewal date; see
/// openspecs/payments.md.
/// </summary>
[ApiController]
[Authorize]
[Route("api/payments")]
public class PaymentsController(
    IPaymentService paymentService,
    IMemberService memberService,
    IMemberUploadService memberUploadService) : ControllerBase
{
    /// <summary>Admin queue. Defaults to Submitted, which is the only status that needs action.</summary>
    [HttpGet]
    [RequirePermission(Permissions.Members.View)]
    public async Task<ActionResult<PagedResult<PaymentDto>>> GetAll(
        int page = 1, int pageSize = 20, PaymentStatus? status = PaymentStatus.Submitted,
        CancellationToken cancellationToken = default) =>
        Ok(await paymentService.GetAllAsync(page, pageSize, status, cancellationToken));

    /// <summary>The caller's own payment history, including rejected ones and why.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<IReadOnlyList<PaymentDto>>> GetMine(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var member = await memberService.GetByUserIdAsync(userId.Value, cancellationToken);
        return member is null
            ? Ok(Array.Empty<PaymentDto>())
            : Ok(await paymentService.GetForMemberAsync(member.Id, cancellationToken));
    }

    [HttpGet("member/{memberId:guid}")]
    [RequirePermission(Permissions.Members.View)]
    public async Task<ActionResult<IReadOnlyList<PaymentDto>>> GetForMember(Guid memberId, CancellationToken cancellationToken) =>
        Ok(await paymentService.GetForMemberAsync(memberId, cancellationToken));

    [HttpPost("me")]
    public async Task<ActionResult<PaymentDto>> Submit(SubmitPaymentRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await paymentService.SubmitAsync(userId.Value, request, cancellationToken);
        return result.Succeeded
            ? Ok(result.Value)
            : result.ErrorType switch
            {
                ResultErrorType.NotFound => NotFound(new { message = result.Error }),
                ResultErrorType.Conflict => Conflict(new { message = result.Error }),
                _ => BadRequest(new { message = result.Error }),
            };
    }

    /// <summary>
    /// Attaches the deposit slip / transfer screenshot to the caller's own pending payment. Separate
    /// from Submit because it's multipart - the same split the member document uploads already use.
    /// </summary>
    [HttpPost("{id:guid}/proof")]
    public async Task<IActionResult> UploadProof(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var ownership = await EnsureOwnPaymentAsync(id, userId.Value, cancellationToken);
        if (ownership is not null)
        {
            return ownership;
        }

        await using var stream = file.OpenReadStream();
        var stored = await memberUploadService.UploadPaymentProofAsync(userId.Value, stream, file.FileName, file.Length, cancellationToken);
        if (!stored.Succeeded)
        {
            return BadRequest(new { message = stored.Error });
        }

        return ToActionResult(await paymentService.AttachProofAsync(id, stored.Value!, cancellationToken));
    }

    /// <summary>Serves a payment's proof. Members may only fetch their own; staff need members:view.</summary>
    [HttpGet("{id:guid}/proof")]
    public async Task<IActionResult> GetProof(Guid id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var payment = await paymentService.GetByIdAsync(id, cancellationToken);
        if (payment is null || !payment.HasProof)
        {
            return NotFound();
        }

        var member = await memberService.GetByIdAsync(payment.MemberId, cancellationToken);
        var isOwner = member is not null && member.UserId == userId.Value;
        if (!isOwner && !User.HasClaim(Permissions.ClaimType, Permissions.Members.View))
        {
            return Forbid();
        }

        // Fetched separately rather than carried on PaymentDto: the storage key embeds the member's
        // surname, first name and birthdate, which has no business being in an API response.
        var key = await paymentService.GetProofKeyAsync(id, cancellationToken);
        var file = key is null ? null : await memberUploadService.OpenByKeyAsync(key, cancellationToken);
        return file is null ? NotFound() : File(file.Value.Content, file.Value.ContentType);
    }

    [HttpPost("{id:guid}/verify")]
    [RequirePermission(Permissions.Members.Manage)]
    public async Task<IActionResult> Verify(Guid id, CancellationToken cancellationToken)
    {
        var decidedBy = CurrentUserId;
        if (decidedBy is null)
        {
            return Unauthorized();
        }

        return ToActionResult(await paymentService.VerifyAsync(id, decidedBy.Value, cancellationToken));
    }

    [HttpPost("{id:guid}/reject")]
    [RequirePermission(Permissions.Members.Manage)]
    public async Task<IActionResult> Reject(Guid id, RejectPaymentRequest request, CancellationToken cancellationToken)
    {
        var decidedBy = CurrentUserId;
        if (decidedBy is null)
        {
            return Unauthorized();
        }

        return ToActionResult(await paymentService.RejectAsync(id, request.Reason, decidedBy.Value, cancellationToken));
    }

    /// <summary>Readable by any authenticated caller - the registration wizard shows the total to
    /// an applicant who has no permissions at all yet.</summary>
    [HttpGet("fees")]
    public async Task<ActionResult<MembershipFeesDto>> GetFees(CancellationToken cancellationToken) =>
        Ok(await paymentService.GetFeesAsync(cancellationToken));

    [HttpPut("fees")]
    [RequirePermission(Permissions.Members.Manage)]
    public async Task<IActionResult> UpdateFees(UpdateMembershipFeesRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await paymentService.UpdateFeesAsync(request, cancellationToken));

    /// <summary>Null when the payment is the caller's own; an error result otherwise.</summary>
    private async Task<IActionResult?> EnsureOwnPaymentAsync(Guid paymentId, Guid userId, CancellationToken cancellationToken)
    {
        var payment = await paymentService.GetByIdAsync(paymentId, cancellationToken);
        if (payment is null)
        {
            return NotFound();
        }

        var member = await memberService.GetByIdAsync(payment.MemberId, cancellationToken);
        return member is not null && member.UserId == userId ? null : Forbid();
    }

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

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
            ResultErrorType.Conflict => Conflict(new { message = result.Error }),
            _ => BadRequest(new { message = result.Error }),
        };
    }
}
