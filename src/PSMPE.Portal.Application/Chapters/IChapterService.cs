using PSMPE.Portal.Application.Common.Models;

namespace PSMPE.Portal.Application.Chapters;

public interface IChapterService
{
    /// <summary>Search is a case-insensitive name match; isActive null returns both.</summary>
    Task<IReadOnlyList<ChapterDto>> GetAllAsync(string? search = null, bool? isActive = null, CancellationToken cancellationToken = default);

    Task<Result<ChapterDto>> CreateAsync(SaveChapterRequest request, CancellationToken cancellationToken = default);

    Task<Result<ChapterDto>> UpdateAsync(Guid id, SaveChapterRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
