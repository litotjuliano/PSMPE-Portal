using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Seed;

/// <summary>
/// Loads the PSGC-derived Philippine locations (embedded ph-locations.json) into the database the
/// first time, so the address picker has data on every environment. Seeds only into an empty
/// table: once a Super Admin starts editing, a redeploy must never put back what they removed.
/// </summary>
public static class LocationSeeder
{
    private sealed record Row(
        [property: JsonPropertyName("region")] string Region,
        [property: JsonPropertyName("province")] string Province,
        [property: JsonPropertyName("city")] string City,
        [property: JsonPropertyName("zip_code")] string? ZipCode);

    public static async Task SeedAsync(ApplicationDbContext db, ILogger logger)
    {
        if (await db.PhRegions.AnyAsync()) return;

        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ph-locations.json")
            ?? throw new InvalidOperationException("Embedded ph-locations.json is missing.");
        var rows = await JsonSerializer.DeserializeAsync<List<Row>>(stream) ?? [];

        var regions = new Dictionary<string, PhRegion>();
        var provinces = new Dictionary<(string, string), PhProvince>();
        var cityKeys = new HashSet<(string, string, string)>();

        foreach (var row in rows)
        {
            if (!regions.TryGetValue(row.Region, out var region))
            {
                // File order is the intended Region I ... NCR order, so keep it.
                region = new PhRegion { Name = row.Region, SortOrder = regions.Count + 1 };
                regions[row.Region] = region;
                db.PhRegions.Add(region);
            }

            if (!provinces.TryGetValue((row.Region, row.Province), out var province))
            {
                province = new PhProvince { Name = row.Province, Region = region };
                provinces[(row.Region, row.Province)] = province;
                db.PhProvinces.Add(province);
            }

            if (!cityKeys.Add((row.Region, row.Province, row.City.ToLowerInvariant()))) continue;
            db.PhCities.Add(new PhCity { Name = row.City, ZipCode = row.ZipCode, Province = province });
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Regions} regions, {Provinces} provinces and {Cities} cities.",
            regions.Count, provinces.Count, cityKeys.Count);
    }
}
