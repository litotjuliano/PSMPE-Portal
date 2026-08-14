using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.WebAPI.Controllers;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Errors;

public class ErrorsControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ErrorsControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string UniqueIp() => AuthTestHelpers.NextClientIp(secondOctet: 22);

    [Fact]
    public async Task ReportFrontendError_Unauthenticated_IsAccepted_AndWritesNullUserId()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/errors/frontend")
        {
            Content = JsonContent.Create(new ErrorsController.FrontendErrorReportRequest("Cannot read properties of undefined", "at Foo (bar.js:1:1)", "https://example.com/members", null))
        };
        request.Headers.Add("X-Forwarded-For", UniqueIp());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.ErrorLogs, e => e.Url == "https://example.com/members");
        Assert.Equal(ErrorSource.Frontend, row.Source);
        Assert.Null(row.UserId);
    }

    [Fact]
    public async Task ReportFrontendError_OversizedStackTrace_IsAcceptedAndTruncated()
    {
        var marker = Guid.NewGuid().ToString("N");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/errors/frontend")
        {
            Content = JsonContent.Create(new ErrorsController.FrontendErrorReportRequest(marker, new string('s', 9000), null, null))
        };
        request.Headers.Add("X-Forwarded-For", UniqueIp());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.ErrorLogs, e => e.Message == marker);
        Assert.Equal(8000, row.StackTrace!.Length);
    }

    [Fact]
    public async Task ReportFrontendError_OversizedComponentStack_IsAcceptedAndTruncated()
    {
        var marker = Guid.NewGuid().ToString("N");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/errors/frontend")
        {
            Content = JsonContent.Create(new ErrorsController.FrontendErrorReportRequest(marker, null, null, new string('c', 9000)))
        };
        request.Headers.Add("X-Forwarded-For", UniqueIp());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.ErrorLogs, e => e.Message == marker);
        using var metadata = JsonDocument.Parse(row.Metadata!);
        Assert.Equal(4000, metadata.RootElement.GetProperty("componentStack").GetString()!.Length);
    }

    [Fact]
    public async Task ReportFrontendError_Authenticated_RecordsUserId()
    {
        using var setupScope = _factory.Services.CreateScope();
        var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var (userId, token) = await _client.CreatePrivilegedUserAsync(userManager, RoleNames.Admin);
        var marker = Guid.NewGuid().ToString("N");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/errors/frontend")
        {
            Content = JsonContent.Create(new ErrorsController.FrontendErrorReportRequest(marker, null, null, null))
        }.WithBearer(token);
        request.Headers.Add("X-Forwarded-For", UniqueIp());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.ErrorLogs, e => e.Message == marker);
        Assert.Equal(userId, row.UserId);
    }

    [Fact]
    public async Task ReportFrontendError_ExceedingRateLimit_Returns429()
    {
        var ip = UniqueIp();
        for (var i = 0; i < 31; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/errors/frontend")
            {
                Content = JsonContent.Create(new ErrorsController.FrontendErrorReportRequest($"error {i}", null, null, null))
            };
            request.Headers.Add("X-Forwarded-For", ip);
            await _client.SendAsync(request);
        }

        var request31 = new HttpRequestMessage(HttpMethod.Post, "/api/errors/frontend")
        {
            Content = JsonContent.Create(new ErrorsController.FrontendErrorReportRequest("one too many", null, null, null))
        };
        request31.Headers.Add("X-Forwarded-For", ip);
        var response = await _client.SendAsync(request31);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }
}
