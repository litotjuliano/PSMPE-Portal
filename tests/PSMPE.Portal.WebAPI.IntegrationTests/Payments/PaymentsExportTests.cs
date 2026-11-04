using System.Net;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Payments;

public class PaymentsExportTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string Url = "/api/payments/export";
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PaymentsExportTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid UserId, string Token)> UserAsync(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await _client.CreatePrivilegedUserAsync(userManager, role);
    }

    private Task<HttpResponseMessage> GetAsync(string token) =>
        _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, Url).WithBearer(token));

    [Fact]
    public async Task Export_Anonymous_Returns401()
    {
        var response = await _client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Export_WithoutMembersView_Returns403()
    {
        var (_, token) = await UserAsync(RoleNames.Member);

        var response = await GetAsync(token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_WithMembersView_ReturnsWorkbookAndRecordsAudit()
    {
        var (userId, token) = await UserAsync(RoleNames.Approval);

        var response = await GetAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal("Full Name", workbook.Worksheet(1).Cell(1, 1).GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = await db.AuditLogs.SingleAsync(a => a.EventType == "payments.exported" && a.ActorUserId == userId);
        Assert.Contains("\"rows\":", audit.Metadata);
    }
}
