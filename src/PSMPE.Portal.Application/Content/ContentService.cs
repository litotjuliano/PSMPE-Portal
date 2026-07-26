using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Caching;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Content.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Content;

public class ContentService(IApplicationDbContext db, ICurrentUserService currentUser, ICacheService? cache = null) : IContentService
{
    // Cache is optional so the 4 existing unit tests that construct ContentService directly
    // (new ContentService(db, fakeUser)) keep compiling unchanged and get no-op caching - only
    // the real DI-resolved MemoryCacheService (production) actually caches. See docs/caching-strategy.md.
    private ICacheService Cache => cache ?? NoOpCacheService.Instance;
    private const string AllCacheKey = "content:all";
    private static string ItemCacheKey(Guid id) => $"content:{id}";

    private static ContentItemDto ToDto(ContentItem item) => new(
        item.Id, item.Title, item.Body, item.Status, item.OwnerId, item.LayoutId, item.CreatedAt, item.UpdatedAt);

    private bool IsAdmin => currentUser.IsInRole(RoleNames.Admin) || currentUser.IsInRole(RoleNames.SuperAdmin);

    public Task<IReadOnlyList<ContentItemDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Cache.GetOrCreateAsync(AllCacheKey, "Cache:ContentDurationSeconds", 300, async () =>
        {
            var items = await db.ContentItems.AsNoTracking().ToListAsync(cancellationToken);
            return (IReadOnlyList<ContentItemDto>)items.Select(ToDto).ToList();
        });

    public Task<ContentItemDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Cache.GetOrCreateAsync(ItemCacheKey(id), "Cache:ContentDurationSeconds", 300, async () =>
        {
            var item = await db.ContentItems.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            return item is null ? null : ToDto(item);
        });

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
        Cache.Remove(AllCacheKey);
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
        Cache.Remove(AllCacheKey);
        Cache.Remove(ItemCacheKey(id));
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
        Cache.Remove(AllCacheKey);
        Cache.Remove(ItemCacheKey(id));
        return Result.Success();
    }
}
