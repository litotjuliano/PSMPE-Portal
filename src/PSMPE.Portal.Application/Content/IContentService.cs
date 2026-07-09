using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Content.Dtos;

namespace PSMPE.Portal.Application.Content;

public interface IContentService
{
    Task<IReadOnlyList<ContentItemDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ContentItemDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ContentItemDto> CreateAsync(CreateContentItemRequest request, CancellationToken cancellationToken = default);
    Task<Result> UpdateAsync(Guid id, UpdateContentItemRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
