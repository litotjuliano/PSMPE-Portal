using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Application.Chapters;

public class ChapterService(IApplicationDbContext db) : IChapterService
{
    private const int MaxNameLength = 64;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<IReadOnlyList<ChapterDto>> GetAllAsync(
        string? search = null, bool? isActive = null, CancellationToken cancellationToken = default)
    {
        var query = db.Chapters.AsNoTracking();
        if (Clean(search) is { } term)
        {
            var lowered = term.ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(lowered));
        }
        if (isActive is not null)
        {
            query = query.Where(c => c.IsActive == isActive);
        }

        return await query.OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new ChapterDto(
                c.Id, c.Name, c.SortOrder, c.IsActive,
                db.Members.Count(m => m.ChapterId == c.Id),
                db.Events.Count(e => e.ChapterId == c.Id)))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<ChapterDto>> CreateAsync(SaveChapterRequest request, CancellationToken cancellationToken = default)
    {
        var name = Clean(request.Name);
        if (name is null) return Result<ChapterDto>.Failure("Chapter name is required.");
        if (name.Length > MaxNameLength) return Result<ChapterDto>.Failure($"Chapter name must be {MaxNameLength} characters or fewer.");
        if (await db.Chapters.AnyAsync(c => c.Name.ToLower() == name.ToLower(), cancellationToken))
            return Result<ChapterDto>.Conflict($"Chapter '{name}' already exists.");

        var sortOrder = request.SortOrder
            ?? (await db.Chapters.AnyAsync(cancellationToken) ? await db.Chapters.MaxAsync(c => c.SortOrder, cancellationToken) + 1 : 1);
        var chapter = new Chapter { Name = name, SortOrder = sortOrder, IsActive = request.IsActive ?? true };
        db.Chapters.Add(chapter);
        await db.SaveChangesAsync(cancellationToken);
        return Result<ChapterDto>.Success(new ChapterDto(chapter.Id, chapter.Name, chapter.SortOrder, chapter.IsActive, 0, 0));
    }

    public async Task<Result<ChapterDto>> UpdateAsync(Guid id, SaveChapterRequest request, CancellationToken cancellationToken = default)
    {
        var chapter = await db.Chapters.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (chapter is null) return Result<ChapterDto>.NotFound($"Chapter '{id}' was not found.");
        var name = Clean(request.Name);
        if (name is null) return Result<ChapterDto>.Failure("Chapter name is required.");
        if (name.Length > MaxNameLength) return Result<ChapterDto>.Failure($"Chapter name must be {MaxNameLength} characters or fewer.");
        if (await db.Chapters.AnyAsync(c => c.Id != id && c.Name.ToLower() == name.ToLower(), cancellationToken))
            return Result<ChapterDto>.Conflict($"Chapter '{name}' already exists.");

        // Members and events reference the row by id, so a rename shows up everywhere at once.
        chapter.Name = name;
        chapter.SortOrder = request.SortOrder ?? chapter.SortOrder;
        chapter.IsActive = request.IsActive ?? chapter.IsActive;
        chapter.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var members = await db.Members.CountAsync(m => m.ChapterId == id, cancellationToken);
        var events = await db.Events.CountAsync(e => e.ChapterId == id, cancellationToken);
        return Result<ChapterDto>.Success(new ChapterDto(chapter.Id, chapter.Name, chapter.SortOrder, chapter.IsActive, members, events));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var chapter = await db.Chapters.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (chapter is null) return Result.NotFound($"Chapter '{id}' was not found.");

        var members = await db.Members.CountAsync(m => m.ChapterId == id, cancellationToken);
        var events = await db.Events.CountAsync(e => e.ChapterId == id, cancellationToken);
        if (members > 0 || events > 0)
            return Result.Conflict(
                $"'{chapter.Name}' is in use by {members} member(s) and {events} event(s). Deactivate it instead of deleting it.");

        db.Chapters.Remove(chapter);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
