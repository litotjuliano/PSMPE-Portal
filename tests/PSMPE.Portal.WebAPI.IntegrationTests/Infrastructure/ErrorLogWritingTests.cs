using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Infrastructure;

public class ErrorLogWritingTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ErrorLogWritingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UnhandledBackendException_WritesErrorLogRow_AndStillReturns500()
    {
        var response = await _client.GetAsync("/api/diagnostics/throw");

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.ErrorLogs);
        Assert.Equal(ErrorSource.Backend, row.Source);
        Assert.Equal("System.InvalidOperationException", row.ExceptionType);
        Assert.Equal("/api/diagnostics/throw", row.RequestPath);
        Assert.Equal("GET", row.RequestMethod);
    }

    [Fact]
    public async Task UnhandledBackendException_ForAuthenticatedCaller_RecordsUserIdAndUserAgent()
    {
        using var setupScope = _factory.Services.CreateScope();
        var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var (userId, token) = await _client.CreatePrivilegedUserAsync(userManager, RoleNames.Admin);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/diagnostics/throw").WithBearer(token);
        request.Headers.UserAgent.ParseAdd("IntegrationTest/1.0");
        var response = await _client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.ErrorLogs, e => e.UserId == userId);
        Assert.Equal("IntegrationTest/1.0", row.UserAgent);
    }
}
