using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Features.CreateAsset;
using HR.Modules.Assets.Features.CreateAssetCategory;
using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

public class CreateAssetHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    private static AssetsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AssetsDbContext(options);
    }

    private static async Task<(Guid categoryId, Guid companyId)> SeedActiveCategoryAsync(AssetsDbContext db)
    {
        var companyId = Guid.NewGuid();
        var categoryHandler = new CreateAssetCategoryHandler(db, new FakeClock(FixedUtcNow));
        var categoryResult = await categoryHandler.HandleAsync(new CreateAssetCategoryRequest
        {
            CompanyId = companyId,
            Name = "Electronics",
            Description = null
        }, CancellationToken.None);

        return (categoryResult.Value!.Id, companyId);
    }

    [Fact]
    public async Task HandleAsync_Creates_Asset_With_All_Fields()
    {
        await using var db = BuildContext();
        var (categoryId, companyId) = await SeedActiveCategoryAsync(db);
        var handler = new CreateAssetHandler(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyAssetNumberSettingsReader(), new FakeAssetNumberGenerator());

        var result = await handler.HandleAsync(new CreateAssetRequest
        {
            CompanyId = companyId,
            AssetNumber = "ASSET-001",
            CategoryId = categoryId,
            Name = "Laptop",
            Manufacturer = "Dell",
            Model = "XPS 15",
            SerialNumber = "SN123456",
            PurchaseDate = new DateOnly(2024, 1, 15),
            PurchasePrice = 1500.00m
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEqual(Guid.Empty, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal("ASSET-001", result.Value.AssetNumber);
        Assert.Equal(categoryId, result.Value.CategoryId);
        Assert.Equal("Laptop", result.Value.Name);
        Assert.Equal("Dell", result.Value.Manufacturer);
        Assert.Equal("XPS 15", result.Value.Model);
        Assert.Equal("SN123456", result.Value.SerialNumber);
        Assert.Equal(new DateOnly(2024, 1, 15), result.Value.PurchaseDate);
        Assert.Equal(1500.00m, result.Value.PurchasePrice);
        Assert.Equal("Available", result.Value.Status);
    }

    [Fact]
    public async Task HandleAsync_Creates_Asset_With_Optional_Fields_Null()
    {
        await using var db = BuildContext();
        var (categoryId, companyId) = await SeedActiveCategoryAsync(db);
        var handler = new CreateAssetHandler(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyAssetNumberSettingsReader(), new FakeAssetNumberGenerator());

        var result = await handler.HandleAsync(new CreateAssetRequest
        {
            CompanyId = companyId,
            AssetNumber = "ASSET-002",
            CategoryId = categoryId,
            Name = "Monitor",
            Manufacturer = null,
            Model = null,
            SerialNumber = null,
            PurchaseDate = null,
            PurchasePrice = null
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Manufacturer);
        Assert.Null(result.Value.Model);
        Assert.Null(result.Value.SerialNumber);
        Assert.Null(result.Value.PurchaseDate);
        Assert.Null(result.Value.PurchasePrice);
    }

    [Fact]
    public async Task HandleAsync_Sets_Status_To_Available()
    {
        await using var db = BuildContext();
        var (categoryId, companyId) = await SeedActiveCategoryAsync(db);
        var handler = new CreateAssetHandler(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyAssetNumberSettingsReader(), new FakeAssetNumberGenerator());

        var result = await handler.HandleAsync(new CreateAssetRequest
        {
            CompanyId = companyId,
            AssetNumber = "ASSET-003",
            CategoryId = categoryId,
            Name = "Keyboard"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Available", result.Value!.Status);
    }

    [Fact]
    public async Task HandleAsync_Sets_CreatedAt_And_UpdatedAt()
    {
        await using var db = BuildContext();
        var (categoryId, companyId) = await SeedActiveCategoryAsync(db);
        var handler = new CreateAssetHandler(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyAssetNumberSettingsReader(), new FakeAssetNumberGenerator());

        var result = await handler.HandleAsync(new CreateAssetRequest
        {
            CompanyId = companyId,
            AssetNumber = "ASSET-004",
            CategoryId = categoryId,
            Name = "Mouse"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), result.Value!.CreatedAt);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), result.Value.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Persists_Asset_To_Database()
    {
        await using var db = BuildContext();
        var (categoryId, companyId) = await SeedActiveCategoryAsync(db);
        var handler = new CreateAssetHandler(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyAssetNumberSettingsReader(), new FakeAssetNumberGenerator());

        await handler.HandleAsync(new CreateAssetRequest
        {
            CompanyId = companyId,
            AssetNumber = "ASSET-005",
            CategoryId = categoryId,
            Name = "Webcam"
        }, CancellationToken.None);

        var saved = await db.Assets.SingleAsync(a => a.AssetNumber == "ASSET-005");
        Assert.Equal(companyId, saved.CompanyId);
        Assert.Equal(categoryId, saved.CategoryId);
        Assert.Equal("Webcam", saved.Name);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_AssetNumber_Already_Exists()
    {
        await using var db = BuildContext();
        var (categoryId, companyId) = await SeedActiveCategoryAsync(db);
        var handler = new CreateAssetHandler(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyAssetNumberSettingsReader(), new FakeAssetNumberGenerator());

        var request = new CreateAssetRequest
        {
            CompanyId = companyId,
            AssetNumber = "DUPLICATE",
            CategoryId = categoryId,
            Name = "First Asset"
        };
        await handler.HandleAsync(request, CancellationToken.None);

        var result = await handler.HandleAsync(request with { Name = "Second Asset" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_AssetNumber_In_Different_Companies()
    {
        await using var db = BuildContext();
        var (categoryId1, companyId1) = await SeedActiveCategoryAsync(db);
        var (categoryId2, companyId2) = await SeedActiveCategoryAsync(db);
        var handler = new CreateAssetHandler(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyAssetNumberSettingsReader(), new FakeAssetNumberGenerator());

        await handler.HandleAsync(new CreateAssetRequest
        {
            CompanyId = companyId1,
            AssetNumber = "SHARED-001",
            CategoryId = categoryId1,
            Name = "Asset in Company 1"
        }, CancellationToken.None);

        var result = await handler.HandleAsync(new CreateAssetRequest
        {
            CompanyId = companyId2,
            AssetNumber = "SHARED-001",
            CategoryId = categoryId2,
            Name = "Asset in Company 2"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = new CreateAssetHandler(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyAssetNumberSettingsReader(), new FakeAssetNumberGenerator());

        var result = await handler.HandleAsync(new CreateAssetRequest
        {
            CompanyId = Guid.NewGuid(),
            AssetNumber = "ASSET-006",
            CategoryId = Guid.NewGuid(),
            Name = "Orphan Asset"
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var (categoryId, _) = await SeedActiveCategoryAsync(db);
        var handler = new CreateAssetHandler(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyAssetNumberSettingsReader(), new FakeAssetNumberGenerator());

        var result = await handler.HandleAsync(new CreateAssetRequest
        {
            CompanyId = Guid.NewGuid(), // different company
            AssetNumber = "ASSET-007",
            CategoryId = categoryId,
            Name = "Cross-tenant Asset"
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Publishes_AssetCreated_Audit_Event_On_Success()
    {
        await using var db = BuildContext();
        var (categoryId, companyId) = await SeedActiveCategoryAsync(db);
        var auditPublisher = new FakeAuditPublisher();
        var handler = new CreateAssetHandler(db, new FakeClock(FixedUtcNow), auditPublisher, new FakeCompanyAssetNumberSettingsReader(), new FakeAssetNumberGenerator());

        var result = await handler.HandleAsync(new CreateAssetRequest
        {
            CompanyId = companyId,
            AssetNumber = "AUDIT-001",
            CategoryId = categoryId,
            Name = "Audited Laptop"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.Single(auditPublisher.Published);
        Assert.Equal("asset.created", evt.EventType);
        Assert.Equal(result.Value!.Id, evt.EntityId);
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Null(evt.ActorUserId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_On_Failure()
    {
        await using var db = BuildContext();
        var (categoryId, companyId) = await SeedActiveCategoryAsync(db);
        var auditPublisher = new FakeAuditPublisher();
        var handler = new CreateAssetHandler(db, new FakeClock(FixedUtcNow), auditPublisher, new FakeCompanyAssetNumberSettingsReader(), new FakeAssetNumberGenerator());

        var request = new CreateAssetRequest
        {
            CompanyId = companyId,
            AssetNumber = "DUPE",
            CategoryId = categoryId,
            Name = "First"
        };
        await handler.HandleAsync(request, CancellationToken.None);
        auditPublisher.Published.ToList(); // reset not needed — just verify count below

        var auditPublisher2 = new FakeAuditPublisher();
        var handler2 = new CreateAssetHandler(db, new FakeClock(FixedUtcNow), auditPublisher2, new FakeCompanyAssetNumberSettingsReader(), new FakeAssetNumberGenerator());
        var result = await handler2.HandleAsync(request with { Name = "Duplicate" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(auditPublisher2.Published);
    }
}
