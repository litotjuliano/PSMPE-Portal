using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Application.UnitTests.TestSupport;

public class FakeCurrentUserService(Guid? userId, params string[] roles) : ICurrentUserService
{
    public Guid? UserId { get; } = userId;
    public IReadOnlyList<string> Roles { get; } = roles;
    public bool IsInRole(string role) => Roles.Contains(role);
}
