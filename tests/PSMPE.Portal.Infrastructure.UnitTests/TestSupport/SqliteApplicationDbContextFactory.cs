using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Infrastructure.Persistence;

namespace PSMPE.Portal.Infrastructure.UnitTests.TestSupport;

/// <summary>
/// A real relational ApplicationDbContext backed by an in-memory SQLite database, for the one
/// class of test EF Core's InMemory provider can't support: ExecuteUpdateAsync
/// (MembershipLifecycleService's auto-flip) throws under InMemory, but SQLite is a real relational
/// provider and handles it like Postgres would. EnsureCreated (not migrations) is enough for a
/// throwaway test database - the connection must stay open for the context's lifetime, since a
/// SQLite ":memory:" database is destroyed the moment its only connection closes.
/// </summary>
public sealed class SqliteApplicationDbContextFactory : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteApplicationDbContextFactory()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new ApplicationDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
