using HR.Modules.Assets.Features.CreateAsset;
using HR.Modules.Assets.Features.CreateAssetAssignment;
using HR.Modules.Assets.Features.CreateAssetCategory;
using HR.Modules.Assets.Features.GetAssetAssignment;
using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

public class GetAssetAssignmentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    private static AssetsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AssetsDbContext(options);
    }

    private static async Task<(Guid companyId, Guid assetId, Guid assignmentId)> SeedAssignmentAsync(
        AssetsDbContext db,
        string? notes = null)
    {
        var companyId = Guid.NewGuid();
        var clock = new FakeClock(FixedUtcNow);
        var taskCreator = new FakeTaskCreator();

        var categoryHandler = new CreateAssetCategoryHandler(db, clock);
        var categoryResult = await categoryHandler.HandleAsync(new CreateAssetCategoryRequest
        {
            CompanyId = companyId,
            Name = "Electronics"
        }, CancellationToken.None);

        var assetHandler = new CreateAssetHandler(db, clock, new FakeAuditPublisher(), new FakeCompanyAssetNumberSettingsReader(), new FakeAssetNumberGenerator());
        var assetResult = await assetHandler.HandleAsync(new CreateAssetRequest
        {
            CompanyId = companyId,
            AssetNumber = "ASSET-001",
            CategoryId = categoryResult.Value!.Id,
            Name = "Laptop"
        }, CancellationToken.None);

        var assetId = assetResult.Value!.Id;

        var assignmentHandler = new CreateAssetAssignmentHandler(db, clock, taskCreator, new FakeNotificationWriter(), new FakeAuditPublisher());
        var assignmentResult = await assignmentHandler.HandleAsync(new CreateAssetAssignmentRequest
        {
            CompanyId = companyId,
            AssetId = assetId,
            EmployeeId = Guid.NewGuid(),
            AssignedBy = Guid.NewGuid(),
            Notes = notes
        }, CancellationToken.None);

        return (companyId, assetId, assignmentResult.Value!.Id);
    }

    [Fact]
    public async Task HandleAsync_Returns_Assignment_When_Found()
    {
        await using var db = BuildContext();
        var (companyId, assetId, assignmentId) = await SeedAssignmentAsync(db);
        var handler = new GetAssetAssignmentHandler(db);

        var result = await handler.HandleAsync(new GetAssetAssignmentRequest
        {
            CompanyId = companyId,
            AssetId = assetId,
            Id = assignmentId
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(assignmentId, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal(assetId, result.Value.AssetId);
        Assert.True(result.Value.IsActive);
        Assert.Null(result.Value.ReturnedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Assignment_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = new GetAssetAssignmentHandler(db);

        var result = await handler.HandleAsync(new GetAssetAssignmentRequest
        {
            CompanyId = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            Id = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Assignment_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var (_, assetId, assignmentId) = await SeedAssignmentAsync(db);
        var handler = new GetAssetAssignmentHandler(db);

        var result = await handler.HandleAsync(new GetAssetAssignmentRequest
        {
            CompanyId = Guid.NewGuid(), // different company
            AssetId = assetId,
            Id = assignmentId
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Assignment_Belongs_To_Different_Asset()
    {
        await using var db = BuildContext();
        var (companyId, _, assignmentId) = await SeedAssignmentAsync(db);
        var handler = new GetAssetAssignmentHandler(db);

        var result = await handler.HandleAsync(new GetAssetAssignmentRequest
        {
            CompanyId = companyId,
            AssetId = Guid.NewGuid(), // different asset
            Id = assignmentId
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Notes_When_Present()
    {
        await using var db = BuildContext();
        var (companyId, assetId, assignmentId) = await SeedAssignmentAsync(db, notes: "Handle with care");
        var handler = new GetAssetAssignmentHandler(db);

        var result = await handler.HandleAsync(new GetAssetAssignmentRequest
        {
            CompanyId = companyId,
            AssetId = assetId,
            Id = assignmentId
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Handle with care", result.Value!.Notes);
    }
}
