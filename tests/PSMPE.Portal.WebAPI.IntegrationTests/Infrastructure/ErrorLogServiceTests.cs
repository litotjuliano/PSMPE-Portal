using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Infrastructure;

public class ErrorLogServiceTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public ErrorLogServiceTests(CustomWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RecordAsync_PersistsAllFields()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IErrorLogService>();
        var userId = Guid.NewGuid();

        await service.RecordAsync(
            ErrorSource.Backend, "System.InvalidOperationException", "boom", "at Foo.Bar()",
            "/api/members", "POST", url: null, userId, "TestAgent/1.0", metadata: null);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.ErrorLogs, e => e.UserId == userId);
        Assert.Equal(ErrorSource.Backend, row.Source);
        Assert.Equal("System.InvalidOperationException", row.ExceptionType);
        Assert.Equal("boom", row.Message);
        Assert.Equal("at Foo.Bar()", row.StackTrace);
        Assert.Equal("/api/members", row.RequestPath);
        Assert.Equal("POST", row.RequestMethod);
        Assert.Null(row.Url);
        Assert.Equal("TestAgent/1.0", row.UserAgent);
        Assert.Null(row.Metadata);
    }

    [Fact]
    public async Task RecordAsync_TruncatesOversizedMessageAndStackTrace()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IErrorLogService>();
        var marker = Guid.NewGuid().ToString("N");

        await service.RecordAsync(
            ErrorSource.Frontend, exceptionType: null, marker + new string('m', 3000), new string('s', 9000),
            requestPath: null, requestMethod: null, url: "https://example.com/", userId: null,
            userAgent: null, metadata: null);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.ErrorLogs, e => e.Message.StartsWith(marker));
        Assert.Equal(2000, row.Message.Length);
        Assert.Equal(8000, row.StackTrace!.Length);
    }

    [Fact]
    public async Task RecordAsync_TruncatesOversizedUserAgentRequestPathUrlAndRequestMethod()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IErrorLogService>();
        var marker = Guid.NewGuid().ToString("N");

        await service.RecordAsync(
            ErrorSource.Frontend, exceptionType: null, marker, stackTrace: null,
            requestPath: new string('p', 600), requestMethod: new string('m', 100),
            url: new string('u', 600), userId: null, userAgent: new string('a', 600), metadata: null);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.ErrorLogs, e => e.Message == marker);
        Assert.Equal(512, row.RequestPath!.Length);
        Assert.Equal(16, row.RequestMethod!.Length);
        Assert.Equal(512, row.Url!.Length);
        Assert.Equal(512, row.UserAgent!.Length);
    }

    [Fact]
    public async Task RecordAsync_WhenSaveFails_DoesNotThrow()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.DisposeAsync();
        var service = scope.ServiceProvider.GetRequiredService<IErrorLogService>();

        var exception = await Record.ExceptionAsync(() =>
            service.RecordAsync(ErrorSource.Backend, null, "boom", null, null, null, null, null, null, null));

        Assert.Null(exception);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersBySourceAndSearchesMessage()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var marker = Guid.NewGuid().ToString("N");
        db.ErrorLogs.AddRange(
            new ErrorLog { Source = ErrorSource.Backend, Message = marker + " Null reference in MemberService" },
            new ErrorLog { Source = ErrorSource.Frontend, Message = "Cannot read properties of undefined " + marker });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IErrorLogService>();

        // Source + marker together narrow to exactly the Backend row seeded above, regardless of
        // how many other ErrorLog rows accumulate from other tests sharing this fixture's database.
        var backendOnly = await service.GetPagedAsync(1, 20, search: marker, source: ErrorSource.Backend, from: null, to: null);
        Assert.Single(backendOnly.Items);

        // "undefined <marker>" as one contiguous substring only appears in the Frontend row above -
        // the Backend row's marker isn't adjacent to "undefined", and no other test's message can
        // contain this specific marker.
        var searched = await service.GetPagedAsync(1, 20, search: "undefined " + marker, source: null, from: null, to: null);
        Assert.Single(searched.Items);
    }

    [Fact]
    public async Task GetPagedAsync_OrdersNewestFirst()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var marker = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        db.ErrorLogs.AddRange(
            new ErrorLog { Source = ErrorSource.Backend, Message = $"{marker}-oldest", CreatedAt = now.AddDays(-3) },
            new ErrorLog { Source = ErrorSource.Backend, Message = $"{marker}-middle", CreatedAt = now.AddDays(-2) },
            new ErrorLog { Source = ErrorSource.Backend, Message = $"{marker}-newest", CreatedAt = now.AddDays(-1) });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IErrorLogService>();

        // search: marker scopes results to just this test's own seeded rows, since other tests'
        // ErrorLog rows share this fixture's database and source/from/to alone wouldn't isolate.
        var result = await service.GetPagedAsync(page: 1, pageSize: 20, search: marker, source: null, from: null, to: null);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal($"{marker}-newest", result.Items[0].Message);
        Assert.Equal($"{marker}-oldest", result.Items[2].Message);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByDateRange()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var marker = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        db.ErrorLogs.AddRange(
            new ErrorLog { Source = ErrorSource.Backend, Message = $"{marker}-in-range-1", CreatedAt = now.AddDays(-1) },
            new ErrorLog { Source = ErrorSource.Backend, Message = $"{marker}-in-range-2", CreatedAt = now.AddDays(-2) },
            new ErrorLog { Source = ErrorSource.Backend, Message = $"{marker}-out-of-range", CreatedAt = now.AddDays(-3) });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IErrorLogService>();

        var result = await service.GetPagedAsync(
            page: 1, pageSize: 20, search: marker, source: null,
            from: now.AddDays(-2).AddHours(-1), to: now.AddDays(-1).AddHours(1));

        Assert.Equal(2, result.TotalCount); // excludes this test's own -3 day row
    }
}
