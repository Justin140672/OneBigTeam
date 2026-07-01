using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Features.ListAssetAssignments;
using HR.Modules.Assets.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

public class ListAssetAssignmentsHandlerTests
{
    private static readonly DateTimeOffset FixedOffset = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    private static AssetsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AssetsDbContext(options);
    }

    private static Asset SeedAsset(AssetsDbContext db, Guid companyId, string assetNumber = "A001")
    {
        var category = AssetCategory.Create(Guid.NewGuid(), companyId, "IT Equipment", null, FixedOffset);
        db.AssetCategories.Add(category);
        var asset = Asset.Create(Guid.NewGuid(), companyId, assetNumber, category.Id, "Laptop", "Dell", "XPS 15", "SN-001", null, null, FixedOffset);
        db.Assets.Add(asset);
        return asset;
    }

    private static AssetAssignment SeedAssignment(AssetsDbContext db, Guid companyId, Guid assetId, Guid employeeId, string? notes = null, DateTimeOffset? returnedAt = null)
    {
        var assignment = AssetAssignment.Create(Guid.NewGuid(), companyId, assetId, employeeId, Guid.NewGuid(), notes, FixedOffset);
        if (returnedAt.HasValue)
            assignment.Return(returnedAt.Value);
        db.AssetAssignments.Add(assignment);
        return assignment;
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Assignments_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var asset = SeedAsset(db, companyId);
        await db.SaveChangesAsync();

        var handler = new ListAssetAssignmentsHandler(db);
        var result = await handler.HandleAsync(
            new ListAssetAssignmentsRequest { CompanyId = companyId, AssetId = asset.Id },
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_Returns_Assignment_For_Asset()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var asset = SeedAsset(db, companyId);
        SeedAssignment(db, companyId, asset.Id, employeeId, "Some notes");
        await db.SaveChangesAsync();

        var handler = new ListAssetAssignmentsHandler(db);
        var result = await handler.HandleAsync(
            new ListAssetAssignmentsRequest { CompanyId = companyId, AssetId = asset.Id },
            CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(asset.Id, result[0].AssetId);
        Assert.Equal(employeeId, result[0].EmployeeId);
        Assert.Equal("Some notes", result[0].Notes);
        Assert.True(result[0].IsActive);
        Assert.Null(result[0].ReturnedAt);
    }

    [Fact]
    public async Task HandleAsync_Includes_Returned_Assignments()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var asset = SeedAsset(db, companyId);
        SeedAssignment(db, companyId, asset.Id, employeeId, returnedAt: FixedOffset.AddDays(7));
        await db.SaveChangesAsync();

        var handler = new ListAssetAssignmentsHandler(db);
        var result = await handler.HandleAsync(
            new ListAssetAssignmentsRequest { CompanyId = companyId, AssetId = asset.Id },
            CancellationToken.None);

        Assert.Single(result);
        Assert.NotNull(result[0].ReturnedAt);
        Assert.False(result[0].IsActive);
    }

    [Fact]
    public async Task HandleAsync_Returns_Multiple_Assignments_In_Descending_Order()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var asset = SeedAsset(db, companyId);

        var employee1 = Guid.NewGuid();
        var employee2 = Guid.NewGuid();

        // Add earlier assignment first
        var a1 = AssetAssignment.Create(Guid.NewGuid(), companyId, asset.Id, employee1, Guid.NewGuid(), null, FixedOffset.AddDays(-10));
        a1.Return(FixedOffset.AddDays(-1));
        db.AssetAssignments.Add(a1);

        var a2 = AssetAssignment.Create(Guid.NewGuid(), companyId, asset.Id, employee2, Guid.NewGuid(), null, FixedOffset);
        db.AssetAssignments.Add(a2);

        await db.SaveChangesAsync();

        var handler = new ListAssetAssignmentsHandler(db);
        var result = await handler.HandleAsync(
            new ListAssetAssignmentsRequest { CompanyId = companyId, AssetId = asset.Id },
            CancellationToken.None);

        Assert.Equal(2, result.Count);
        // Most recent first
        Assert.Equal(employee2, result[0].EmployeeId);
        Assert.Equal(employee1, result[1].EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Assignments_For_Other_Assets()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var asset1 = SeedAsset(db, companyId, "A001");
        var asset2 = SeedAsset(db, companyId, "A002");

        SeedAssignment(db, companyId, asset1.Id, Guid.NewGuid());
        await db.SaveChangesAsync();

        var handler = new ListAssetAssignmentsHandler(db);
        var result = await handler.HandleAsync(
            new ListAssetAssignmentsRequest { CompanyId = companyId, AssetId = asset2.Id },
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Assignments_For_Other_Companies()
    {
        await using var db = BuildContext();
        var companyId1 = Guid.NewGuid();
        var companyId2 = Guid.NewGuid();
        var asset = SeedAsset(db, companyId1);
        SeedAssignment(db, companyId1, asset.Id, Guid.NewGuid());
        await db.SaveChangesAsync();

        var handler = new ListAssetAssignmentsHandler(db);
        var result = await handler.HandleAsync(
            new ListAssetAssignmentsRequest { CompanyId = companyId2, AssetId = asset.Id },
            CancellationToken.None);

        Assert.Empty(result);
    }
}
