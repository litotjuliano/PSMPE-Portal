using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Application.Auth;

public interface IJwtTokenGenerator
{
    (string Token, DateTimeOffset ExpiresAt) GenerateToken(ApplicationUser user, IList<string> roles);
}
