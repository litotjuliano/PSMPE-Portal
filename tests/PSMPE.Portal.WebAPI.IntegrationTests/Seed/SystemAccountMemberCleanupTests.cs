using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.Infrastructure.Persistence.Seed;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Seed;

/// <summary>
/// Exercises SystemAccountMemberCleanup directly against the real ApplicationDbContext/UserManager
/// (backed by the InMemory database from CustomWebApplicationFactory) - same convention as the
/// other integration tests in this project.
/// </summary>
public class SystemAccountMemberCleanupTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public SystemAccountMemberCleanupTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _scope.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Member> SeedMemberForRoleAsync(string role)
    {
        var user = new ApplicationUser
        {
            UserName = $"{Guid.NewGuid()}@example.com",
            Email = $"{Guid.NewGuid()}@example.com",
            DisplayName = "Test User"
        };
        await _userManager.CreateAsync(user, "Password123!");
        await _userManager.AddToRoleAsync(user, role);

        var member = new Member
        {
            UserId = user.Id,
            MembershipNo = Guid.NewGuid().ToString("N")[..8],
            FirstName = "Test",
            LastName = "User",
            Chapter = Chapters.Ncr,
            MemberType = MemberTypes.Regular,
            Status = MembershipStatus.Pending,
            SubmittedAt = DateTimeOffset.UtcNow
        };
        _db.Members.Add(member);
        await _db.SaveChangesAsync();
        return member;
    }

    [Theory]
    [InlineData(RoleNames.SuperAdmin)]
    [InlineData(RoleNames.Admin)]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Accounts)]
    public async Task CleanupAsync_RemovesMemberRowOwnedByAdministrativeAccount(string administrativeRole)
    {
        var badMember = await SeedMemberForRoleAsync(administrativeRole);
        var goodMember = await SeedMemberForRoleAsync(RoleNames.Member);

        await SystemAccountMemberCleanup.CleanupAsync(_db, _userManager, NullLogger.Instance);

        Assert.False(await _db.Members.AsNoTracking().AnyAsync(m => m.Id == badMember.Id));
        Assert.True(await _db.Members.AsNoTracking().AnyAsync(m => m.Id == goodMember.Id));
    }

    [Fact]
    public async Task CleanupAsync_RunTwice_IsIdempotent()
    {
        await SeedMemberForRoleAsync(RoleNames.Admin);

        await SystemAccountMemberCleanup.CleanupAsync(_db, _userManager, NullLogger.Instance);
        var secondRun = Record.ExceptionAsync(() => SystemAccountMemberCleanup.CleanupAsync(_db, _userManager, NullLogger.Instance));

        Assert.Null(await secondRun);
    }
}
