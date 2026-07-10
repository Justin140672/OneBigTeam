using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

public class OutstandingAssetAcknowledgementReaderTests
{
    private static readonly DateTimeOffset FixedOffset = new(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);

    private static AssetsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<AssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static (Asset asset, AssetCategory category) SeedAsset(
        AssetsDbContext db, Guid companyId, string assetNumber = "A001", string name = "Laptop", string categoryName = "IT Equipment")
    {
        var category = AssetCategory.Create(Guid.NewGuid(), companyId, categoryName, null, FixedOffset);
        db.AssetCategories.Add(category);
        var asset = Asset.Create(Guid.NewGuid(), companyId, assetNumber, category.Id, name, "Dell", "XPS 15", "SN-001", null, null, FixedOffset);
        db.Assets.Add(asset);
        return (asset, category);
    }

    private static AssetAssignment SeedAssignment(
        AssetsDbContext db,
        Guid companyId,
        Guid assetId,
        Guid employeeId,
        DateTimeOffset? acknowledgedAt = null,
        DateTimeOffset? returnedAt = null)
    {
        var assignment = AssetAssignment.Create(Guid.NewGuid(), companyId, assetId, employeeId, Guid.NewGuid(), null, FixedOffset);
        if (acknowledgedAt.HasValue)
            assignment.Acknowledge(acknowledgedAt.Value);
        if (returnedAt.HasValue)
            assignment.Return(returnedAt.Value);
        db.AssetAssignments.Add(assignment);
        return assignment;
    }

    [Fact]
    public async Task GetOutstandingAcknowledgementsAsync_Returns_Unacknowledged_Unreturned_Assignments()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var (asset, _) = SeedAsset(db, companyId);

        var outstanding = SeedAssignment(db, companyId, asset.Id, employeeId);
        await db.SaveChangesAsync();

        var reader = new OutstandingAssetAcknowledgementReader(db);
        var result = await reader.GetOutstandingAcknowledgementsAsync(companyId, employeeId, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(outstanding.Id, result[0].AssetAssignmentId);
    }

    [Fact]
    public async Task GetOutstandingAcknowledgementsAsync_Excludes_Acknowledged_Assignments()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var (asset, _) = SeedAsset(db, companyId);

        SeedAssignment(db, companyId, asset.Id, employeeId, acknowledgedAt: FixedOffset.AddDays(1));
        await db.SaveChangesAsync();

        var reader = new OutstandingAssetAcknowledgementReader(db);
        var result = await reader.GetOutstandingAcknowledgementsAsync(companyId, employeeId, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOutstandingAcknowledgementsAsync_Excludes_Returned_Assignments()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var (asset, _) = SeedAsset(db, companyId);

        SeedAssignment(db, companyId, asset.Id, employeeId, returnedAt: FixedOffset.AddDays(1));
        await db.SaveChangesAsync();

        var reader = new OutstandingAssetAcknowledgementReader(db);
        var result = await reader.GetOutstandingAcknowledgementsAsync(companyId, employeeId, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOutstandingAcknowledgementsAsync_Is_Scoped_By_CompanyId()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var (assetA, _) = SeedAsset(db, companyA);
        var (assetB, _) = SeedAsset(db, companyB);

        SeedAssignment(db, companyA, assetA.Id, employeeId);
        SeedAssignment(db, companyB, assetB.Id, employeeId);
        await db.SaveChangesAsync();

        var reader = new OutstandingAssetAcknowledgementReader(db);
        var result = await reader.GetOutstandingAcknowledgementsAsync(companyA, employeeId, CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetOutstandingAcknowledgementsAsync_Returns_Empty_List_When_None_Exist()
    {
        await using var db = BuildContext();

        var reader = new OutstandingAssetAcknowledgementReader(db);
        var result = await reader.GetOutstandingAcknowledgementsAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOutstandingAcknowledgementsAsync_Composes_AssetLabel_From_AssetNumber_Name_And_CategoryName()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var (asset, category) = SeedAsset(db, companyId, "B099", "Monitor", "Peripherals");

        SeedAssignment(db, companyId, asset.Id, employeeId);
        await db.SaveChangesAsync();

        var reader = new OutstandingAssetAcknowledgementReader(db);
        var result = await reader.GetOutstandingAcknowledgementsAsync(companyId, employeeId, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal($"B099 - Monitor ({category.Name})", result[0].AssetLabel);
    }
}
