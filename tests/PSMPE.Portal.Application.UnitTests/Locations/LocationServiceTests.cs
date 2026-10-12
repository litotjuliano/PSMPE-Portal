using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Locations;
using PSMPE.Portal.Application.UnitTests.TestSupport;
using PSMPE.Portal.Domain.Entities;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Locations;

public class LocationServiceTests
{
    private static async Task<(TestDbContext Db, LocationService Service, PhRegion Region, PhProvince Province)> CreateAsync()
    {
        var db = TestDbContext.CreateInMemory();
        var region = new PhRegion { Name = "Region IX (Zamboanga Peninsula)", SortOrder = 1 };
        var province = new PhProvince { Name = "Zamboanga Del Sur", Region = region };
        db.PhRegions.Add(region);
        db.PhProvinces.Add(province);
        db.PhCities.Add(new PhCity { Name = "Pagadian City (Capital)", ZipCode = "7016", Province = province });
        db.PhCities.Add(new PhCity { Name = "Molave", ZipCode = "7023", Province = province });
        await db.SaveChangesAsync();
        return (db, new LocationService(db), region, province);
    }

    [Fact]
    public async Task GetAll_ReturnsFlatRowsOrderedByRegionProvinceCity()
    {
        var (_, service, _, _) = await CreateAsync();

        var rows = await service.GetAllAsync();

        Assert.Equal(["Molave", "Pagadian City (Capital)"], rows.Select(r => r.City));
        Assert.All(rows, r => Assert.Equal("Zamboanga Del Sur", r.Province));
    }

    [Fact]
    public async Task CreateCity_AddsAndAppearsInGetAll()
    {
        var (_, service, _, province) = await CreateAsync();

        var result = await service.CreateCityAsync(new SaveCityRequest("  Zamboanga City ", " 7000 ", province.Id));

        Assert.True(result.Succeeded);
        Assert.Equal("Zamboanga City", result.Value!.Name);
        Assert.Equal("7000", result.Value.ZipCode);
        Assert.Contains(await service.GetAllAsync(), r => r.City == "Zamboanga City");
    }

    [Fact]
    public async Task CreateCity_DuplicateNameInProvince_IgnoringCase_Conflicts()
    {
        var (_, service, _, province) = await CreateAsync();

        var result = await service.CreateCityAsync(new SaveCityRequest("molave", null, province.Id));

        Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task CreateCity_BlankName_Fails()
    {
        var (_, service, _, province) = await CreateAsync();

        var result = await service.CreateCityAsync(new SaveCityRequest("   ", null, province.Id));

        Assert.Equal(ResultErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task UpdateCity_ChangesNameAndZip()
    {
        var (db, service, _, _) = await CreateAsync();
        var molave = db.PhCities.Single(c => c.Name == "Molave");

        var result = await service.UpdateCityAsync(molave.Id, new SaveCityRequest("Molave City", "7099", molave.ProvinceId));

        Assert.True(result.Succeeded);
        Assert.Equal("Molave City", db.PhCities.Single(c => c.Id == molave.Id).Name);
    }

    [Fact]
    public async Task UpdateCity_Missing_NotFound()
    {
        var (_, service, _, province) = await CreateAsync();

        var result = await service.UpdateCityAsync(Guid.NewGuid(), new SaveCityRequest("X", null, province.Id));

        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task DeleteCity_Removes()
    {
        var (db, service, _, _) = await CreateAsync();
        var molave = db.PhCities.Single(c => c.Name == "Molave");

        var result = await service.DeleteCityAsync(molave.Id);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(db.PhCities, c => c.Name == "Molave");
    }

    [Fact]
    public async Task DeleteProvince_WithCities_Conflicts()
    {
        var (_, service, _, province) = await CreateAsync();

        var result = await service.DeleteProvinceAsync(province.Id);

        Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task DeleteRegion_WithProvinces_Conflicts()
    {
        var (_, service, region, _) = await CreateAsync();

        var result = await service.DeleteRegionAsync(region.Id);

        Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task DeleteProvince_AfterItsCitiesAreGone_Succeeds()
    {
        var (db, service, _, province) = await CreateAsync();
        db.PhCities.RemoveRange(db.PhCities);
        await db.SaveChangesAsync();

        var result = await service.DeleteProvinceAsync(province.Id);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CreateRegion_WithoutSortOrder_GoesLast()
    {
        var (_, service, _, _) = await CreateAsync();

        var result = await service.CreateRegionAsync(new SaveRegionRequest("Region XIV", null));

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.SortOrder);
    }

    [Fact]
    public async Task GetCities_FiltersBySearchAndProvince_AndPages()
    {
        var (_, service, _, province) = await CreateAsync();

        var byName = await service.GetCitiesAsync(1, 20, "pagad", null, null);
        var byZip = await service.GetCitiesAsync(1, 20, "7023", null, null);
        var byProvince = await service.GetCitiesAsync(1, 1, null, null, province.Id);
        var otherProvince = await service.GetCitiesAsync(1, 20, null, null, Guid.NewGuid());

        Assert.Equal(["Pagadian City (Capital)"], byName.Items.Select(c => c.Name));
        Assert.Equal(["Molave"], byZip.Items.Select(c => c.Name));
        Assert.Equal(2, byProvince.TotalCount);
        Assert.Single(byProvince.Items);
        Assert.Empty(otherProvince.Items);
    }

    [Fact]
    public async Task GetProvinces_ReportsCityCount()
    {
        var (_, service, region, _) = await CreateAsync();

        var page = await service.GetProvincesAsync(1, 20, "zamboanga", region.Id);

        Assert.Equal(2, page.Items.Single().CityCount);
    }
}
