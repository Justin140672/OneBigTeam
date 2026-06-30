using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Features.ListEmployeeAssets;
using HR.Modules.Assets.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

public class ListEmployeeAssetsHandlerTests
{
    private static readonly DateTimeOffset FixedOffset = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    private static AssetsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AssetsDbContext(options);
    }

    private static Asset SeedAsset(AssetsDbContext db, Guid companyId, string assetNumber = "A001", string name = "Laptop")
    {
        var asset = Asset.Create(Guid.NewGuid(), companyId, assetNumber, Guid.NewGuid(), name, "Dell", "XPS 15", "SN-001", null, null, FixedOffset);
        db.Assets.Add(asset);
        return asset;
    }

    private static AssetAssignment SeedAssignment(AssetsDbContext db, Guid companyId, Guid assetId, Guid employeeId, DateTimeOffset? returnedAt = null)
    {
        var assignment = AssetAssignment.Create(Guid.NewGuid(), companyId, assetId, employeeId, Guid.NewGuid(), "Test notes", FixedOffset);
        if (returnedAt.HasValue)
            assignment.Return(returnedAt.Value);
        db.AssetAssignments.Add(assignment);
        return assignment;
    }

    [Fact]
    public async Task HandleAsync_Returns_Active_Assignments_For_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var asset = SeedAsset(db, companyId);
        SeedAssignment(db, companyId, asset.Id, employeeId);
        await db.SaveChangesAsync();

        var handler = new ListEmployeeAssetsHandler(db);
        var result = await handler.HandleAsync(
            new ListEmployeeAssetsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(employeeId, result[0].EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Returned_Assignments()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var asset = SeedAsset(db, companyId);
        SeedAssignment(db, companyId, asset.Id, employeeId, returnedAt: FixedOffset.AddDays(1));
        await db.SaveChangesAsync();

        var handler = new ListEmployeeAssetsHandler(db);
        var result = await handler.HandleAsync(
            new ListEmployeeAssetsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Assignments_For_Other_Employees()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();

        var asset = SeedAsset(db, companyId);
        SeedAssignment(db, companyId, asset.Id, otherEmployeeId);
        await db.SaveChangesAsync();

        var handler = new ListEmployeeAssetsHandler(db);
        var result = await handler.HandleAsync(
            new ListEmployeeAssetsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Assignments_From_Other_Companies()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var asset = SeedAsset(db, otherCompanyId);
        SeedAssignment(db, otherCompanyId, asset.Id, employeeId);
        await db.SaveChangesAsync();

        var handler = new ListEmployeeAssetsHandler(db);
        var result = await handler.HandleAsync(
            new ListEmployeeAssetsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_Returns_Results_Ordered_By_AssignedAt_Descending()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var asset1 = SeedAsset(db, companyId, "A001", "Laptop");
        var asset2 = SeedAsset(db, companyId, "A002", "Desk");

        var earlier = FixedOffset.AddDays(-1);
        var later = FixedOffset;

        db.AssetAssignments.Add(AssetAssignment.Create(Guid.NewGuid(), companyId, asset1.Id, employeeId, Guid.NewGuid(), null, earlier));
        db.AssetAssignments.Add(AssetAssignment.Create(Guid.NewGuid(), companyId, asset2.Id, employeeId, Guid.NewGuid(), null, later));
        await db.SaveChangesAsync();

        var handler = new ListEmployeeAssetsHandler(db);
        var result = await handler.HandleAsync(
            new ListEmployeeAssetsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(later, result[0].AssignedAt);
        Assert.Equal(earlier, result[1].AssignedAt);
    }

    [Fact]
    public async Task HandleAsync_Maps_Asset_Fields_From_Join()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var asset = Asset.Create(Guid.NewGuid(), companyId, "B099", Guid.NewGuid(), "Monitor", "LG", "27UN850", "SN-XYZ", null, null, FixedOffset);
        db.Assets.Add(asset);
        var assignment = AssetAssignment.Create(Guid.NewGuid(), companyId, asset.Id, employeeId, Guid.NewGuid(), "Handle carefully", FixedOffset);
        db.AssetAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var handler = new ListEmployeeAssetsHandler(db);
        var result = await handler.HandleAsync(
            new ListEmployeeAssetsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Single(result);
        var item = result[0];
        Assert.Equal(assignment.Id, item.Id);
        Assert.Equal(asset.Id, item.AssetId);
        Assert.Equal(employeeId, item.EmployeeId);
        Assert.Equal("Handle carefully", item.Notes);
        Assert.Equal("B099", item.AssetNumber);
        Assert.Equal("Monitor", item.Name);
        Assert.Equal("LG", item.Manufacturer);
        Assert.Equal("27UN850", item.Model);
        Assert.Equal("SN-XYZ", item.SerialNumber);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Assignments_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var handler = new ListEmployeeAssetsHandler(db);
        var result = await handler.HandleAsync(
            new ListEmployeeAssetsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Empty(result);
    }
}
