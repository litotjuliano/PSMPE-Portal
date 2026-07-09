using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Layouts.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Layouts;

public class LayoutService(IApplicationDbContext db, ICurrentUserService currentUser) : ILayoutService
{
    private static LayoutDto ToDto(Layout layout) => new(
        layout.Id, layout.Name, layout.Definition, layout.IsSystemLayout, layout.OwnerId);

    public async Task<IReadOnlyList<LayoutDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var layouts = await db.Layouts.AsNoTracking().ToListAsync(cancellationToken);
        return layouts.Select(ToDto).ToList();
    }

    public async Task<LayoutDto> CreateAsync(CreateLayoutRequest request, CancellationToken cancellationToken = default)
    {
        var layout = new Layout
        {
            Name = request.Name,
            Definition = request.Definition,
            IsSystemLayout = false,
            OwnerId = currentUser.UserId
        };

        db.Layouts.Add(layout);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(layout);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var layout = await db.Layouts.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (layout is null)
        {
            return Result.NotFound($"Layout '{id}' was not found.");
        }

        // System layouts ship with the platform and may only be removed by a Super Admin.
        if (layout.IsSystemLayout && !currentUser.IsInRole(RoleNames.SuperAdmin))
        {
            return Result.Forbidden("System layouts can only be deleted by a Super Admin.");
        }

        if (!layout.IsSystemLayout && layout.OwnerId != currentUser.UserId
            && !currentUser.IsInRole(RoleNames.Admin) && !currentUser.IsInRole(RoleNames.SuperAdmin))
        {
            return Result.Forbidden("You can only delete layouts you own.");
        }

        db.Layouts.Remove(layout);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
