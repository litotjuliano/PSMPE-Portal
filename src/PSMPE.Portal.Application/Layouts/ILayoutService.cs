using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Layouts.Dtos;

namespace PSMPE.Portal.Application.Layouts;

public interface ILayoutService
{
    Task<IReadOnlyList<LayoutDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<LayoutDto> CreateAsync(CreateLayoutRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
