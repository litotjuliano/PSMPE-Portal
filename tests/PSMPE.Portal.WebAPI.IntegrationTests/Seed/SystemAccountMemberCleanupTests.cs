using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
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
            ChapterId = TestChapters.Ncr,
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

    // Regression: production crash-looped at startup because an Admin account had a Member row with a
    // Payment; removing it violated FK_Payments_Members_MemberId and took the whole API down. Money
    // records are never deleted by a self-healing startup job - the row is kept and left for an admin.
    [Fact]
    public async Task CleanupAsync_KeepsAdministrativeMemberThatHasPayments_AndDoesNotThrow()
    {
        var memberWithPayment = await SeedMemberForRoleAsync(RoleNames.Admin);
        _db.Payments.Add(new Payment
        {
            MemberId = memberWithPayment.Id,
            Kind = PaymentKind.NewMembership,
            Amount = 100m,
            PaidOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = PaymentStatus.Submitted
        });
        await _db.SaveChangesAsync();
        var memberWithoutPayment = await SeedMemberForRoleAsync(RoleNames.Admin);

        var exception = await Record.ExceptionAsync(() =>
            SystemAccountMemberCleanup.CleanupAsync(_db, _userManager, NullLogger.Instance));

        Assert.Null(exception);
        Assert.True(await _db.Members.AsNoTracking().AnyAsync(m => m.Id == memberWithPayment.Id));
        Assert.True(await _db.Payments.AsNoTracking().AnyAsync(p => p.MemberId == memberWithPayment.Id));
        Assert.False(await _db.Members.AsNoTracking().AnyAsync(m => m.Id == memberWithoutPayment.Id));
    }

    // Admin + Member is a real member who also has admin access (they apply, pay and get approved
    // like anyone else), not leftover data - their profile must survive every restart even when it
    // has no payments yet.
    [Fact]
    public async Task CleanupAsync_KeepsMemberRowOfAdminWhoAlsoHoldsMemberRole()
    {
        var dualRoleMember = await SeedMemberForRoleAsync(RoleNames.Admin);
        var dualRoleUser = await _userManager.FindByIdAsync(dualRoleMember.UserId.ToString());
        await _userManager.AddToRoleAsync(dualRoleUser!, RoleNames.Member);

        await SystemAccountMemberCleanup.CleanupAsync(_db, _userManager, NullLogger.Instance);

        Assert.True(await _db.Members.AsNoTracking().AnyAsync(m => m.Id == dualRoleMember.Id));
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
