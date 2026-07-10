namespace PSMPE.Portal.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsInRole(string role);
    bool HasPermission(string permission);
}
