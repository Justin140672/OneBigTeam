using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Features.CreateAsset;
using HR.Modules.Assets.Features.CreateAssetAssignment;
using HR.Modules.Assets.Features.CreateAssetCategory;
using HR.Modules.Assets.Features.ReturnAssetAssignment;
using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

public class AssetReturnServiceTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    private static AssetsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AssetsDbContext(options);
    }

    private static async Task<(Guid assignmentId, Guid assetId, Guid employeeId, Guid companyId)> SeedActiveAssignmentAsync(AssetsDbContext db)
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var clock = new FakeClock(FixedUtcNow);

        var categoryResult = await new CreateAssetCategoryHandler(db, clock).HandleAsync(
            new CreateAssetCategoryRequest { CompanyId = companyId, Name = "IT" },
            CancellationToken.None);

        var assetResult = await new CreateAssetHandler(db, clock, new FakeAuditPublisher(), new FakeCompanyAssetNumberSettingsReader(), new FakeAssetNumberGenerator()).HandleAsync(
            new CreateAssetRequest
            {
                CompanyId = companyId,
                AssetNumber = "ASSET-001",
                CategoryId = categoryResult.Value!.Id,
                Name = "Laptop"
            }, CancellationToken.None);

        var assignmentResult = await new CreateAssetAssignmentHandler(db, clock, new FakeTaskCreator(), new FakeNotificationWriter(), new FakeAuditPublisher()).HandleAsync(
            new CreateAssetAssignmentRequest
            {
                CompanyId = companyId,
                AssetId = assetResult.Value!.Id,
                EmployeeId = employeeId,
                AssignedBy = Guid.NewGuid()
            }, CancellationToken.None);

        return (assignmentResult.Value!.Id, assetResult.Value.Id, employeeId, companyId);
    }

    [Fact]
    public async Task ReturnAsync_Sets_ReturnedAt_On_Assignment()
    {
        await using var db = BuildContext();
        var (assignmentId, _, _, companyId) = await SeedActiveAssignmentAsync(db);
        var clock = new FakeClock(FixedUtcNow);
        var service = new AssetReturnService(db, clock, new FakeAuditPublisher());

        await service.ReturnAsync(companyId, assignmentId, Guid.NewGuid(), CancellationToken.None);

        var assignment = await db.AssetAssignments.FindAsync(assignmentId);
        Assert.NotNull(assignment!.ReturnedAt);
        Assert.False(assignment.IsActive);
    }

    [Fact]
    public async Task ReturnAsync_Marks_Asset_As_Available()
    {
        await using var db = BuildContext();
        var (assignmentId, assetId, _, companyId) = await SeedActiveAssignmentAsync(db);
        var clock = new FakeClock(FixedUtcNow);
        var service = new AssetReturnService(db, clock, new FakeAuditPublisher());

        await service.ReturnAsync(companyId, assignmentId, Guid.NewGuid(), CancellationToken.None);

        var asset = await db.Assets.FindAsync(assetId);
        Assert.Equal(AssetStatus.Available, asset!.Status);
    }

    [Fact]
    public async Task ReturnAsync_Publishes_Audit_Event()
    {
        await using var db = BuildContext();
        var (assignmentId, _, employeeId, companyId) = await SeedActiveAssignmentAsync(db);
        var clock = new FakeClock(FixedUtcNow);
        var auditPublisher = new FakeAuditPublisher();
        var returnedBy = Guid.NewGuid();
        var service = new AssetReturnService(db, clock, auditPublisher);

        await service.ReturnAsync(companyId, assignmentId, returnedBy, CancellationToken.None);

        var evt = Assert.Single(auditPublisher.Published);
        Assert.Equal("asset.assignment.returned", evt.EventType);
        Assert.Equal(assignmentId, evt.EntityId);
        Assert.Equal(returnedBy, evt.ActorUserId);
        Assert.Equal(employeeId, evt.ActorEmployeeId);
    }

    [Fact]
    public async Task ReturnAsync_Is_Idempotent_When_Already_Returned()
    {
        await using var db = BuildContext();
        var (assignmentId, _, _, companyId) = await SeedActiveAssignmentAsync(db);
        var clock = new FakeClock(FixedUtcNow);
        var auditPublisher = new FakeAuditPublisher();
        var service = new AssetReturnService(db, clock, auditPublisher);

        await service.ReturnAsync(companyId, assignmentId, Guid.NewGuid(), CancellationToken.None);
        await service.ReturnAsync(companyId, assignmentId, Guid.NewGuid(), CancellationToken.None);

        Assert.Single(auditPublisher.Published);
    }

    [Fact]
    public async Task ReturnAsync_Does_Nothing_When_Assignment_Not_Found()
    {
        await using var db = BuildContext();
        var clock = new FakeClock(FixedUtcNow);
        var auditPublisher = new FakeAuditPublisher();
        var service = new AssetReturnService(db, clock, auditPublisher);

        await service.ReturnAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task ReturnAsync_Does_Nothing_When_Assignment_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var (assignmentId, _, _, _) = await SeedActiveAssignmentAsync(db);
        var clock = new FakeClock(FixedUtcNow);
        var auditPublisher = new FakeAuditPublisher();
        var service = new AssetReturnService(db, clock, auditPublisher);

        await service.ReturnAsync(Guid.NewGuid(), assignmentId, Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(auditPublisher.Published);
        var assignment = await db.AssetAssignments.FindAsync(assignmentId);
        Assert.True(assignment!.IsActive);
    }
}
