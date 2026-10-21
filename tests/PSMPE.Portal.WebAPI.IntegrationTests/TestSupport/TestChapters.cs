using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;

/// <summary>The six chapters production starts with (see the AddChaptersAndOptionalEventDates
/// migration), under fixed ids so tests can reference them without a lookup. Seeded into every
/// test database by CustomWebApplicationFactory.InitializeAsync.</summary>
public static class TestChapters
{
    public static readonly Guid Ncr = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    public static readonly Guid Cebu = Guid.Parse("00000000-0000-0000-0000-0000000000c2");
    public static readonly Guid Davao = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
    public static readonly Guid Baguio = Guid.Parse("00000000-0000-0000-0000-0000000000c4");
    public static readonly Guid Cavite = Guid.Parse("00000000-0000-0000-0000-0000000000c5");
    public static readonly Guid QuezonCity = Guid.Parse("00000000-0000-0000-0000-0000000000c6");

    public const string NcrName = "NCR";
    public const string CebuName = "Cebu";
    public const string DavaoName = "Davao";
    public const string BaguioName = "Baguio";
    public const string CaviteName = "Cavite";
    public const string QuezonCityName = "Quezon City Chapter";

    /// <summary>In sort order - what the dashboard's ByChapter is expected to follow.</summary>
    public static readonly string[] AllNames = [NcrName, CebuName, DavaoName, BaguioName, CaviteName, QuezonCityName];

    public static IEnumerable<Chapter> BuildAll() =>
    [
        new() { Id = Ncr, Name = NcrName, SortOrder = 1 },
        new() { Id = Cebu, Name = CebuName, SortOrder = 2 },
        new() { Id = Davao, Name = DavaoName, SortOrder = 3 },
        new() { Id = Baguio, Name = BaguioName, SortOrder = 4 },
        new() { Id = Cavite, Name = CaviteName, SortOrder = 5 },
        new() { Id = QuezonCity, Name = QuezonCityName, SortOrder = 6 },
    ];
}
