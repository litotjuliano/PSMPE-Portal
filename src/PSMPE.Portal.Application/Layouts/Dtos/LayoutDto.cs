namespace PSMPE.Portal.Application.Layouts.Dtos;

public record LayoutDto(Guid Id, string Name, string Definition, bool IsSystemLayout, Guid? OwnerId);

public record CreateLayoutRequest(string Name, string Definition);
