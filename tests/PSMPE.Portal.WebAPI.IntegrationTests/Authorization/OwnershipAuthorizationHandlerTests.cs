using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Authorization;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Authorization;

public class OwnershipAuthorizationHandlerTests
{
    private static ClaimsPrincipal BuildUser(Guid userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static async Task<bool> AuthorizeAsync(ClaimsPrincipal user, ContentItem resource)
    {
        var handler = new OwnershipAuthorizationHandler();
        var requirement = new OwnershipRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, resource);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task Owner_IsAuthorized()
    {
        var userId = Guid.NewGuid();
        var resource = new ContentItem { OwnerId = userId };

        Assert.True(await AuthorizeAsync(BuildUser(userId, RoleNames.ContentCreator), resource));
    }

    [Fact]
    public async Task NonOwnerNonAdmin_IsNotAuthorized()
    {
        var resource = new ContentItem { OwnerId = Guid.NewGuid() };

        Assert.False(await AuthorizeAsync(BuildUser(Guid.NewGuid(), RoleNames.ContentCreator), resource));
    }

    [Fact]
    public async Task Admin_IsAuthorized_RegardlessOfOwnership()
    {
        var resource = new ContentItem { OwnerId = Guid.NewGuid() };

        Assert.True(await AuthorizeAsync(BuildUser(Guid.NewGuid(), RoleNames.Admin), resource));
    }
}
