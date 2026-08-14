using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Authorization.Policies;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>
/// Read-only views over AuditLog/ErrorLog, restricted to Super Admin - stricter than the general
/// RequireAdminOrApproval gate on the rest of /api/admin, matching how role assignment and user
/// deletion are already Super-Admin-only in AdminController.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = PolicyNames.RequireSuperAdmin)]
public class SystemLogsController(
    IAuditLogService auditLogService,
    IErrorLogService errorLogService,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    public record AuditLogEntryDto(
        Guid Id, string EventType, Guid? ActorUserId, string? ActorEmail, string? ActorIp,
        string? TargetType, Guid? TargetId, string? Metadata, DateTimeOffset CreatedAt);

    [HttpGet("audit-log")]
    public async Task<ActionResult<PagedResult<AuditLogEntryDto>>> GetAuditLog(
        int page = 1, int pageSize = 20, string? search = null, string? eventType = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        var result = await auditLogService.GetPagedAsync(page, pageSize, search, eventType, from, to, cancellationToken);
        var actorEmails = await ResolveEmailsAsync(result.Items.Select(a => a.ActorUserId));

        var entries = result.Items
            .Select(a => new AuditLogEntryDto(
                a.Id, a.EventType, a.ActorUserId,
                a.ActorUserId is { } actorId ? actorEmails.GetValueOrDefault(actorId) : null,
                a.ActorIp, a.TargetType, a.TargetId, a.Metadata, a.CreatedAt))
            .ToList();

        return Ok(new PagedResult<AuditLogEntryDto>(entries, result.TotalCount, result.Page, result.PageSize));
    }

    /// <summary>Single query for the whole page, same shape as AdminController.GetUsers's role
    /// resolution - not a per-row lookup.</summary>
    private async Task<Dictionary<Guid, string>> ResolveEmailsAsync(IEnumerable<Guid?> userIds)
    {
        var ids = userIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await userManager.Users
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? string.Empty);
    }

    public record ErrorLogEntryDto(
        Guid Id, ErrorSource Source, string? ExceptionType, string Message, string? StackTrace,
        string? RequestPath, string? RequestMethod, string? Url, Guid? UserId, string? UserEmail,
        string? UserAgent, string? Metadata, DateTimeOffset CreatedAt);

    [HttpGet("error-log")]
    public async Task<ActionResult<PagedResult<ErrorLogEntryDto>>> GetErrorLog(
        int page = 1, int pageSize = 20, string? search = null, ErrorSource? source = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        var result = await errorLogService.GetPagedAsync(page, pageSize, search, source, from, to, cancellationToken);
        var userEmails = await ResolveEmailsAsync(result.Items.Select(e => e.UserId));

        var entries = result.Items
            .Select(e => new ErrorLogEntryDto(
                e.Id, e.Source, e.ExceptionType, e.Message, e.StackTrace, e.RequestPath, e.RequestMethod,
                e.Url, e.UserId, e.UserId is { } userId ? userEmails.GetValueOrDefault(userId) : null,
                e.UserAgent, e.Metadata, e.CreatedAt))
            .ToList();

        return Ok(new PagedResult<ErrorLogEntryDto>(entries, result.TotalCount, result.Page, result.PageSize));
    }
}
