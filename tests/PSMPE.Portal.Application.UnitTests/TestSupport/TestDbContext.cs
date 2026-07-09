using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Application.UnitTests.TestSupport;

/// <summary>In-memory-backed stand-in for ApplicationDbContext, used to unit test Application services
/// without pulling in the Infrastructure/Identity/Npgsql dependencies.</summary>
public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<ContentItem> ContentItems => Set<ContentItem>();
    public DbSet<Layout> Layouts => Set<Layout>();
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();

    public static TestDbContext CreateInMemory()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }
}
