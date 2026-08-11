using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Application.Members.Dtos;
using PSMPE.Portal.Application.Payments;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Authorization;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>
/// PSMPE professional membership profiles - distinct from AdminController's Users (login/role
/// accounts). Every Member has exactly one linked ApplicationUser, but not every ApplicationUser
/// has a Member profile (e.g. staff accounts). See openspecs/members.md.
/// </summary>
[ApiController]
[Authorize]
[Route("api/members")]
public class MembersController(
    IMemberService memberService, IMemberUploadService memberUploadService,
    IMemberCertificateService memberCertificateService, UserManager<ApplicationUser> userManager,
    IEmailSender emailSender, IPaymentService paymentService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Members.View)]
    public async Task<ActionResult<PagedResult<MemberDto>>> GetAll(
        int page = 1, int pageSize = 20, string sortBy = "lastName", string sortDir = "asc",
        MembershipStatus? status = null, bool? pendingApprovalOnly = null, bool? pendingPrcVerificationOnly = null,
        string? search = null, CancellationToken cancellationToken = default)
    {
        var excludeUserIds = await GetSystemAccountUserIdsAsync();
        return Ok(await memberService.GetAllAsync(
            page, pageSize, sortBy, sortDir, status, pendingApprovalOnly, pendingPrcVerificationOnly, search, excludeUserIds, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.Members.View)]
    public async Task<ActionResult<MemberDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var member = await memberService.GetByIdAsync(id, cancellationToken);
        if (member is null || await IsSystemAccountAsync(member.UserId))
        {
            return NotFound();
        }

        return Ok(member);
    }

    [HttpGet("me")]
    public async Task<ActionResult<MemberDto>> GetMyProfile(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var member = await memberService.GetByUserIdAsync(userId.Value, cancellationToken);
        return member is null ? NotFound() : Ok(member);
    }

    [HttpPost]
    [RequirePermission(Permissions.Members.Manage)]
    public async Task<ActionResult<MemberDto>> Create(CreateMemberRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            return BadRequest(new { message = $"No user found with id '{request.UserId}'." });
        }

        if (await memberService.GetByUserIdAsync(request.UserId, cancellationToken) is not null)
        {
            return Conflict(new { message = "This user already has a Member profile." });
        }

        // Optional at create - PSMPE assigns its own control number and an admin may not have it
        // yet. It's mandatory at approval instead.
        if (!string.IsNullOrWhiteSpace(request.MembershipNo)
            && await memberService.MembershipNoExistsAsync(request.MembershipNo.Trim(), cancellationToken: cancellationToken))
        {
            return Conflict(new { message = $"Membership ID '{request.MembershipNo.Trim()}' is already in use." });
        }

        var result = await memberService.CreateAsync(request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Members.Manage)]
    public async Task<IActionResult> Update(Guid id, UpdateMemberRequest request, CancellationToken cancellationToken)
    {
        if (await IsHiddenMemberAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var result = await memberService.UpdateAsync(id, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("me")]
    public async Task<ActionResult<MemberDto>> UpdateMyProfile(UpdateMyProfileRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        // Administrative accounts (Super Admin, Admin, Manager, Accounts) don't have membership
        // profiles - only block first-time self-registration (no existing row yet), so this never
        // touches a genuine member's later saves.
        //
        // Carries a message and code rather than a bare Forbid(): Forbid() writes a zero-byte body,
        // so the client had nothing to show and the user saw a silent failure. Note this blocks
        // only the membership *data*; uploads are keyed by UserId, not MemberId, so an
        // administrator can still set their account photo via me/photo.
        if (await memberService.GetByUserIdAsync(userId.Value, cancellationToken) is null
            && await IsSystemAccountAsync(userId.Value))
        {
            return StatusCode(403, new
            {
                message = "Administrative accounts don't have a membership profile. Your account photo can still be updated.",
                code = "ADMIN_ACCOUNT_NO_PROFILE",
            });
        }

        var result = await memberService.UpsertMyProfileAsync(userId.Value, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("me/submit")]
    public async Task<IActionResult> SubmitMyProfile(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await memberService.SubmitMyProfileAsync(userId.Value, cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.Members.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (await IsHiddenMemberAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var result = await memberService.DeleteAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Whether a Membership ID is free, so the approve dialog can say so before an admin commits
    /// rather than after a rejected submit. Advisory only - it can always lose a race with a
    /// concurrent approval, which is why ApproveAsync re-checks and the database holds a
    /// case-insensitive unique index behind both.
    ///
    /// Same Manage permission as approving: it reports whether a control number is taken, which is
    /// not something an unprivileged caller should be able to probe.
    /// </summary>
    [HttpGet("membership-no/availability")]
    [RequirePermission(Permissions.Members.Manage, Permissions.Members.Approve)]
    public async Task<ActionResult<MembershipNoAvailabilityDto>> CheckMembershipNoAvailability(
        string value, Guid? excludeMemberId = null, CancellationToken cancellationToken = default)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > 32)
        {
            // Nothing to look up - the form reports blank/over-length on its own, and querying for
            // them would only produce a misleading "available".
            return Ok(new MembershipNoAvailabilityDto(trimmed, false));
        }

        var taken = await memberService.MembershipNoExistsAsync(trimmed, excludeMemberId, cancellationToken);
        return Ok(new MembershipNoAvailabilityDto(trimmed, !taken));
    }

    [HttpPost("{id:guid}/approve")]
    [RequirePermission(Permissions.Members.Manage, Permissions.Members.Approve)]
    public async Task<IActionResult> Approve(Guid id, ApproveMemberRequest request, CancellationToken cancellationToken)
    {
        if (await IsHiddenMemberAsync(id, cancellationToken))
        {
            return NotFound();
        }

        // Captured before the call so the receipt/email only fire the one time an application
        // actually transitions to approved - ApproveAsync itself is idempotent (a no-op success on
        // an already-approved member), which would otherwise regenerate/resend on every repeat call.
        var wasAlreadyApproved = (await memberService.GetByIdAsync(id, cancellationToken))?.ApprovedAt is not null;

        var decidedBy = CurrentUserId;
        if (decidedBy is null)
        {
            return Unauthorized();
        }

        var result = await memberService.ApproveAsync(id, request, decidedBy.Value, cancellationToken);
        if (result.Succeeded && !wasAlreadyApproved)
        {
            var approvedMember = await memberService.GetByIdAsync(id, cancellationToken);
            if (approvedMember is not null)
            {
                await IssueApprovalReceiptAsync(approvedMember, cancellationToken);
            }
        }

        return ToActionResult(result);
    }

    /// <summary>
    /// Generates the official receipt image, stores it as this member's UploadKind.Receipt (so
    /// "view/download from the dashboard" just re-fetches it later, same as any other member
    /// document), and emails it to the member with the receipt attached. Best-effort: a failure
    /// here doesn't roll back the approval that already succeeded, since the receipt can always be
    /// regenerated by re-fetching GetAsync/re-approving is not needed for that - this is purely a
    /// notification convenience, not part of the approval's correctness.
    /// </summary>
    private async Task IssueApprovalReceiptAsync(MemberDto member, CancellationToken cancellationToken)
    {
        // Fees come from SystemConfig now, so a receipt always shows what PSMPE currently charges.
        var fees = await paymentService.GetFeesAsync(cancellationToken);
        var receiptBytes = ReceiptGenerator.Generate(member, fees);
        await using (var stream = new MemoryStream(receiptBytes))
        {
            await memberUploadService.UploadAsync(member.UserId, UploadKind.Receipt, stream, "receipt.jpg", receiptBytes.Length, cancellationToken);
        }

        var htmlBody =
            $"<p>Hi {member.FirstName},</p>" +
            "<p>Congratulations - your PSMPE membership application has been approved.</p>" +
            $"<p>Your Membership No. is <strong>{member.MembershipNo ?? "-"}</strong>. Your official receipt is attached, " +
            "and is also always available for download from your Dashboard.</p>";

        await emailSender.SendEmailAsync(
            member.Email,
            "Your PSMPE Membership Application Has Been Approved",
            htmlBody,
            cancellationToken,
            attachments: [new EmailAttachment("receipt.jpg", receiptBytes, "image/jpeg")]);
    }

    [HttpPost("{id:guid}/prc-verification/approve")]
    [RequirePermission(Permissions.Members.Manage, Permissions.Members.Approve)]
    public async Task<IActionResult> ApprovePrcVerification(Guid id, CancellationToken cancellationToken)
    {
        var decidedByUserId = CurrentUserId;
        if (decidedByUserId is null)
        {
            return Unauthorized();
        }

        if (await IsHiddenMemberAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var result = await memberService.ApprovePrcVerificationAsync(id, decidedByUserId.Value, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/prc-verification/reject")]
    [RequirePermission(Permissions.Members.Manage, Permissions.Members.Approve)]
    public async Task<IActionResult> RejectPrcVerification(Guid id, RejectPrcVerificationRequest request, CancellationToken cancellationToken)
    {
        var decidedByUserId = CurrentUserId;
        if (decidedByUserId is null)
        {
            return Unauthorized();
        }

        if (await IsHiddenMemberAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var result = await memberService.RejectPrcVerificationAsync(id, request.Reason, decidedByUserId.Value, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("me/photo")]
    public Task<IActionResult> UploadMyPhoto(IFormFile file, CancellationToken cancellationToken) =>
        UploadMyFileAsync(UploadKind.Photo, file, cancellationToken);

    [HttpPost("me/prc-id")]
    public Task<IActionResult> UploadMyPrcId(IFormFile file, CancellationToken cancellationToken) =>
        UploadMyFileAsync(UploadKind.PrcId, file, cancellationToken);

    [HttpPost("me/valid-government-id")]
    public Task<IActionResult> UploadMyValidGovernmentId(IFormFile file, CancellationToken cancellationToken) =>
        UploadMyFileAsync(UploadKind.ValidGovernmentId, file, cancellationToken);

    [HttpPost("me/signature")]
    public Task<IActionResult> UploadMySignature(IFormFile file, CancellationToken cancellationToken) =>
        UploadMyFileAsync(UploadKind.Signature, file, cancellationToken);

    [HttpPost("me/proof-of-payment")]
    public Task<IActionResult> UploadMyProofOfPayment(IFormFile file, CancellationToken cancellationToken) =>
        UploadMyFileAsync(UploadKind.ProofOfPayment, file, cancellationToken);

    [HttpGet("me/photo")]
    public Task<IActionResult> GetMyPhoto(CancellationToken cancellationToken) => GetMyFileAsync(UploadKind.Photo, cancellationToken);

    [HttpGet("me/prc-id")]
    public Task<IActionResult> GetMyPrcId(CancellationToken cancellationToken) => GetMyFileAsync(UploadKind.PrcId, cancellationToken);

    [HttpGet("me/valid-government-id")]
    public Task<IActionResult> GetMyValidGovernmentId(CancellationToken cancellationToken) => GetMyFileAsync(UploadKind.ValidGovernmentId, cancellationToken);

    [HttpGet("me/signature")]
    public Task<IActionResult> GetMySignature(CancellationToken cancellationToken) => GetMyFileAsync(UploadKind.Signature, cancellationToken);

    [HttpGet("me/proof-of-payment")]
    public Task<IActionResult> GetMyProofOfPayment(CancellationToken cancellationToken) => GetMyFileAsync(UploadKind.ProofOfPayment, cancellationToken);

    /// <summary>System-generated on approval (see Approve/IssueApprovalReceiptAsync) - there is no
    /// corresponding POST endpoint, members never upload this themselves.</summary>
    [HttpGet("me/receipt")]
    public Task<IActionResult> GetMyReceipt(CancellationToken cancellationToken) => GetMyFileAsync(UploadKind.Receipt, cancellationToken);

    [HttpGet("{id:guid}/photo")]
    [RequirePermission(Permissions.Members.View)]
    public Task<IActionResult> GetMemberPhoto(Guid id, CancellationToken cancellationToken) => GetMemberFileAsync(id, UploadKind.Photo, cancellationToken);

    [HttpGet("{id:guid}/prc-id")]
    [RequirePermission(Permissions.Members.View)]
    public Task<IActionResult> GetMemberPrcId(Guid id, CancellationToken cancellationToken) => GetMemberFileAsync(id, UploadKind.PrcId, cancellationToken);

    [HttpGet("{id:guid}/valid-government-id")]
    [RequirePermission(Permissions.Members.View)]
    public Task<IActionResult> GetMemberValidGovernmentId(Guid id, CancellationToken cancellationToken) =>
        GetMemberFileAsync(id, UploadKind.ValidGovernmentId, cancellationToken);

    [HttpGet("{id:guid}/signature")]
    [RequirePermission(Permissions.Members.View)]
    public Task<IActionResult> GetMemberSignature(Guid id, CancellationToken cancellationToken) =>
        GetMemberFileAsync(id, UploadKind.Signature, cancellationToken);

    [HttpGet("{id:guid}/proof-of-payment")]
    [RequirePermission(Permissions.Members.View)]
    public Task<IActionResult> GetMemberProofOfPayment(Guid id, CancellationToken cancellationToken) =>
        GetMemberFileAsync(id, UploadKind.ProofOfPayment, cancellationToken);

    [HttpPost("me/certificates")]
    public async Task<IActionResult> UploadMyCertificate(IFormFile file, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        await using var stream = file.OpenReadStream();
        var result = await memberCertificateService.UploadAsync(userId.Value, stream, file.FileName, file.Length, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("me/certificates")]
    public async Task<ActionResult<IReadOnlyList<MemberCertificateDto>>> GetMyCertificates(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        return Ok(await memberCertificateService.ListAsync(userId.Value, cancellationToken));
    }

    [HttpGet("me/certificates/{certificateId:guid}")]
    public async Task<IActionResult> GetMyCertificate(Guid certificateId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var file = await memberCertificateService.GetAsync(userId.Value, certificateId, cancellationToken);
        return file is null ? NotFound() : File(file.Value.Content, file.Value.ContentType, file.Value.FileName);
    }

    [HttpDelete("me/certificates/{certificateId:guid}")]
    public async Task<IActionResult> DeleteMyCertificate(Guid certificateId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await memberCertificateService.DeleteAsync(userId.Value, certificateId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{id:guid}/certificates")]
    [RequirePermission(Permissions.Members.View)]
    public async Task<ActionResult<IReadOnlyList<MemberCertificateDto>>> GetMemberCertificates(Guid id, CancellationToken cancellationToken)
    {
        var member = await memberService.GetByIdAsync(id, cancellationToken);
        if (member is null || await IsSystemAccountAsync(member.UserId))
        {
            return NotFound();
        }

        return Ok(await memberCertificateService.ListAsync(member.UserId, cancellationToken));
    }

    [HttpGet("me/completeness")]
    public async Task<ActionResult<ProfileCompletenessDto>> GetMyProfileCompleteness(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var completeness = await memberService.GetProfileCompletenessAsync(userId.Value, cancellationToken);
        return completeness is null ? NotFound() : Ok(completeness);
    }

    [HttpGet("{id:guid}/completeness")]
    [RequirePermission(Permissions.Members.View)]
    public async Task<ActionResult<ProfileCompletenessDto>> GetMemberProfileCompleteness(Guid id, CancellationToken cancellationToken)
    {
        var member = await memberService.GetByIdAsync(id, cancellationToken);
        if (member is null || await IsSystemAccountAsync(member.UserId))
        {
            return NotFound();
        }

        var completeness = await memberService.GetProfileCompletenessAsync(member.UserId, cancellationToken);
        return completeness is null ? NotFound() : Ok(completeness);
    }

    private async Task<IActionResult> UploadMyFileAsync(UploadKind kind, IFormFile file, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        await using var stream = file.OpenReadStream();
        var result = await memberUploadService.UploadAsync(userId.Value, kind, stream, file.FileName, file.Length, cancellationToken);
        return ToActionResult(result);
    }

    private async Task<IActionResult> GetMyFileAsync(UploadKind kind, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var file = await memberUploadService.GetAsync(userId.Value, kind, cancellationToken);
        return file is null ? NotFound() : File(file.Value.Content, file.Value.ContentType);
    }

    private async Task<IActionResult> GetMemberFileAsync(Guid memberId, UploadKind kind, CancellationToken cancellationToken)
    {
        var member = await memberService.GetByIdAsync(memberId, cancellationToken);
        if (member is null || await IsSystemAccountAsync(member.UserId))
        {
            return NotFound();
        }

        var file = await memberUploadService.GetAsync(member.UserId, kind, cancellationToken);
        return file is null ? NotFound() : File(file.Value.Content, file.Value.ContentType);
    }

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    /// <summary>
    /// Every non-Member role (Super Admin, Admin, Manager, Accounts) is a staff/administrative
    /// account - none of them have membership profiles, so any Member row seen with one of these
    /// UserIds is stale data left by the bug this method's callers guard against.
    /// </summary>
    private async Task<IReadOnlyCollection<Guid>> GetSystemAccountUserIdsAsync()
    {
        var ids = new HashSet<Guid>();
        foreach (var role in RoleNames.All.Where(r => r != RoleNames.Member))
        {
            foreach (var user in await userManager.GetUsersInRoleAsync(role))
            {
                ids.Add(user.Id);
            }
        }

        return ids;
    }

    private async Task<bool> IsSystemAccountAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        var roles = await userManager.GetRolesAsync(user);
        return roles.Any(r => r != RoleNames.Member);
    }

    /// <summary>
    /// Only true when the id resolves to a real Member row owned by an administrative account -
    /// a genuinely unknown id falls through to the calling action so it keeps producing that
    /// action's normal "not found" Result (with its error message), same as before this guard
    /// existed. Unconditional for every caller, including Super Admin - unlike AdminController's
    /// Users list (which only hides Super Admin from lower roles), an administrative account's
    /// Member row should never be reachable via the Members surface by anyone, since it
    /// shouldn't exist.
    /// </summary>
    private async Task<bool> IsHiddenMemberAsync(Guid id, CancellationToken cancellationToken)
    {
        var member = await memberService.GetByIdAsync(id, cancellationToken);
        return member is not null && await IsSystemAccountAsync(member.UserId);
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
            ResultErrorType.Conflict => Conflict(new { message = result.Error }),
            _ => BadRequest(new { message = result.Error })
        };
    }

    private ActionResult<MemberDto> ToActionResult(Result<MemberDto> result)
    {
        if (result.Succeeded)
        {
            return Ok(result.Value);
        }

        return result.ErrorType switch
        {
            ResultErrorType.NotFound => NotFound(new { message = result.Error }),
            ResultErrorType.Forbidden => Forbid(),
            ResultErrorType.Conflict => Conflict(new { message = result.Error }),
            _ => BadRequest(new { message = result.Error })
        };
    }
}
