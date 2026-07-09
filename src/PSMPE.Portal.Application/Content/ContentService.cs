using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Content.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Content;

public class ContentService(IApplicationDbContext db, ICurrentUserService currentUser) : IContentService
{
    private static ContentItemDto ToDto(ContentItem item) => new(
        item.Id, item.Title, item.Body, item.Status, item.OwnerId, item.LayoutId, item.CreatedAt, item.UpdatedAt);

    private bool IsAdmin => currentUser.IsInRole(RoleNames.Admin) || currentUser.IsInRole(RoleNames.SuperAdmin);

    public async Task<IReadOnlyList<ContentItemDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await db.ContentItems.AsNoTracking().ToListAsync(cancellationToken);
        return items.Select(ToDto).ToList();
    }

    public async Task<ContentItemDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await db.ContentItems.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return item is null ? null : ToDto(item);
    }

    public async Task<ContentItemDto> CreateAsync(CreateContentItemRequest request, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is null)
        {
            throw new InvalidOperationException("A content item cannot be created without an authenticated user.");
        }

        var item = new ContentItem
        {
            Title = request.Title,
            Body = request.Body,
            LayoutId = request.LayoutId,
            OwnerId = currentUser.UserId.Value,
            Status = ContentStatus.Draft
        };

        db.ContentItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(item);
    }

    public async Task<Result> UpdateAsync(Guid id, UpdateContentItemRequest request, CancellationToken cancellationToken = default)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (item is null)
        {
            return Result.NotFound($"Content item '{id}' was not found.");
        }

        if (item.OwnerId != currentUser.UserId && !IsAdmin)
        {
            return Result.Forbidden("You can only edit content you own.");
        }

        item.Title = request.Title;
        item.Body = request.Body;
        item.Status = request.Status;
        item.LayoutId = request.LayoutId;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (item is null)
        {
            return Result.NotFound($"Content item '{id}' was not found.");
        }

        if (item.OwnerId != currentUser.UserId && !IsAdmin)
        {
            return Result.Forbidden("You can only delete content you own.");
        }

        db.ContentItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
