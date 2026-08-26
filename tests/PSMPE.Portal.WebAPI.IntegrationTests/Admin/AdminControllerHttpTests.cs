using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.WebAPI.Controllers;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Admin;

/// <summary>
/// Exercises GetUsers's query-string model binding via real HTTP - unlike AdminControllerTests.cs
/// (which instantiates the controller directly and so bypasses ASP.NET Core's model binder
/// entirely), this is what actually catches a parameter that fails to bind from the query string
/// even though it compiles and works fine when called in-process.
///
/// This exists because `roles` (an `IReadOnlyCollection&lt;string&gt;`) silently bound to null
/// without `[FromQuery]` - [ApiController]'s binding-source inference does not treat a bare
/// collection-of-simple-type parameter as query-bound by default, unlike a bare `string`. Every
/// direct-controller-call test passed regardless, because none of them go through binding at all.
/// </summary>
public class AdminControllerHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly HttpClient _client;

    public AdminControllerHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = factory.Services.CreateScope();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _scope.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Two things at once: that the repeated bare-key form binds to the collection at all, and that
    /// several roles are intersected rather than unioned. The binding half is the fragile one - it
    /// broke once before - so it stays covered end to end over real HTTP.
    /// </summary>
    [Fact]
    public async Task GetUsers_OverHttp_WithRepeatedRolesQueryKey_RequiresAllOfThem()
    {
        var (_, token) = await _client.CreatePrivilegedUserAsync(_userManager, RoleNames.SuperAdmin);
        var managerOnly = await _client.CreatePrivilegedUserAsync(_userManager, RoleNames.Manager);
        var accountsOnly = await _client.CreatePrivilegedUserAsync(_userManager, RoleNames.Accounts);
        var both = await _client.CreatePrivilegedUserAsync(_userManager, RoleNames.Manager);

        var bothUser = await _userManager.FindByIdAsync(both.UserId.ToString());
        await _userManager.AddToRoleAsync(bothUser!, RoleNames.Accounts);

        // The repeated bare-key form (`roles=Manager&roles=Accounts`) is what
        // apiClient.ts's paramsSerializer (`indexes: null`) produces for a `roles: string[]` param.
        var request = new HttpRequestMessage(
                HttpMethod.Get, "/api/admin/users?page=1&pageSize=1000&roles=Manager&roles=Accounts")
            .WithBearer(token);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PagedResult<AdminController.UserSummaryDto>>();
        var ids = result!.Items.Select(u => u.Id).ToHashSet();

        Assert.Contains(both.UserId, ids);
        Assert.DoesNotContain(managerOnly.UserId, ids);
        Assert.DoesNotContain(accountsOnly.UserId, ids);
    }
}
