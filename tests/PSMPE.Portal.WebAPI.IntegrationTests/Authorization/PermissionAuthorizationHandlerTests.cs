using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Authorization;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Authorization;

public class PermissionAuthorizationHandlerTests
{
    private static async Task<bool> AuthorizeAsync(ClaimsPrincipal user, string permission)
    {
        var handler = new PermissionAuthorizationHandler();
        var requirement = new PermissionRequirement(permission);
        var context = new AuthorizationHandlerContext([requirement], user, null);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task UserWithMatchingPermissionClaim_IsAuthorized()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(Permissions.ClaimType, Permissions.Content.Create)], "TestAuth"));

        Assert.True(await AuthorizeAsync(user, Permissions.Content.Create));
    }

    [Fact]
    public async Task UserWithoutMatchingPermissionClaim_IsNotAuthorized()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(Permissions.ClaimType, Permissions.Content.Update)], "TestAuth"));

        Assert.False(await AuthorizeAsync(user, Permissions.Content.Create));
    }

    [Fact]
    public async Task UserWithNoClaims_IsNotAuthorized()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity("TestAuth"));

        Assert.False(await AuthorizeAsync(user, Permissions.Content.Create));
    }
}
