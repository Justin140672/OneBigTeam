using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Features.CreateAsset;
using HR.Modules.Assets.Features.CreateAssetAssignment;
using HR.Modules.Assets.Features.CreateAssetCategory;
using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

public class CreateAssetAssignmentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    private static AssetsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AssetsDbContext(options);
    }

    private static async Task<(Guid assetId, Guid companyId)> SeedAvailableAssetAsync(AssetsDbContext db)
    {
        var companyId = Guid.NewGuid();
        var clock = new FakeClock(FixedUtcNow);

        var categoryHandler = new CreateAssetCategoryHandler(db, clock);
        var categoryResult = await categoryHandler.HandleAsync(new CreateAssetCategoryRequest
        {
            CompanyId = companyId,
            Name = "Electronics",
            Description = null
        }, CancellationToken.None);

        var assetHandler = new CreateAssetHandler(db, clock, new FakeAuditPublisher());
        var assetResult = await assetHandler.HandleAsync(new CreateAssetRequest
        {
            CompanyId = companyId,
            AssetNumber = "ASSET-001",
            CategoryId = categoryResult.Value!.Id,
            Name = "Laptop"
        }, CancellationToken.None);

        return (assetResult.Value!.Id, companyId);
    }

    [Fact]
    public async Task HandleAsync_Creates_Assignment_And_Task_When_Asset_Is_Available()
    {
        await using var db = BuildContext();
        var (assetId, companyId) = await SeedAvailableAssetAsync(db);
        var clock = new FakeClock(FixedUtcNow);
        var taskCreator = new FakeTaskCreator();
        var notificationWriter = new FakeNotificationWriter();
        var handler = new CreateAssetAssignmentHandler(db, clock, taskCreator, notificationWriter, new FakeAuditPublisher());
        var employeeId = Guid.NewGuid();
        var assignedBy = Guid.NewGuid();

        var result = await handler.HandleAsync(new CreateAssetAssignmentRequest
        {
            CompanyId = companyId,
            AssetId = assetId,
            EmployeeId = employeeId,
            AssignedBy = assignedBy,
            Notes = "Handle with care"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEqual(Guid.Empty, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal(assetId, result.Value.AssetId);
        Assert.Equal(employeeId, result.Value.EmployeeId);
        Assert.Equal(assignedBy, result.Value.AssignedBy);
        Assert.Equal("Handle with care", result.Value.Notes);

        Assert.Single(taskCreator.Created);
        var task = taskCreator.Created[0];
        Assert.Equal(companyId, task.CompanyId);
        Assert.Equal(assignedBy, task.CreatedBy);
        Assert.Equal("Acknowledge receipt of asset", task.Title);
        Assert.Equal(TaskPriority.Medium, task.Priority);
        Assert.Equal(TaskSource.Asset, task.Source);
        Assert.Equal(TaskActionType.Acknowledge, task.ActionType);
        Assert.Equal(employeeId, task.AssignedEmployeeId);
        Assert.Null(task.AssignedUserId);
        Assert.Equal(result.Value.Id, task.SourceEntityId);
        Assert.Equal(DateOnly.FromDateTime(FixedUtcNow.AddDays(7)), task.DueDate);

        Assert.Single(notificationWriter.Written);
        var notification = notificationWriter.Written[0];
        Assert.Equal(companyId, notification.CompanyId);
        Assert.Equal(employeeId, notification.EmployeeId);
        Assert.Equal("Asset assigned to you", notification.Title);
        Assert.Contains("Laptop", notification.Body!);
        Assert.Equal(result.Value!.Id, notification.SourceEntityId);
        Assert.Equal(NotificationType.AssetAssigned, notification.Type);
        Assert.Equal(NotificationPriority.Normal, notification.Priority);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Asset_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var clock = new FakeClock(FixedUtcNow);
        var taskCreator = new FakeTaskCreator();
        var handler = new CreateAssetAssignmentHandler(db, clock, taskCreator, new FakeNotificationWriter(), new FakeAuditPublisher());

        var result = await handler.HandleAsync(new CreateAssetAssignmentRequest
        {
            CompanyId = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            AssignedBy = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Asset_Is_Not_Available()
    {
        await using var db = BuildContext();
        var (assetId, companyId) = await SeedAvailableAssetAsync(db);
        var clock = new FakeClock(FixedUtcNow);
        var taskCreator = new FakeTaskCreator();
        var handler = new CreateAssetAssignmentHandler(db, clock, taskCreator, new FakeNotificationWriter(), new FakeAuditPublisher());

        // First assignment — should succeed
        var firstResult = await handler.HandleAsync(new CreateAssetAssignmentRequest
        {
            CompanyId = companyId,
            AssetId = assetId,
            EmployeeId = Guid.NewGuid(),
            AssignedBy = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(firstResult.IsSuccess);

        // Second assignment — asset is now Assigned, not Available
        var secondResult = await handler.HandleAsync(new CreateAssetAssignmentRequest
        {
            CompanyId = companyId,
            AssetId = assetId,
            EmployeeId = Guid.NewGuid(),
            AssignedBy = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(secondResult.IsFailure);
        Assert.Equal("conflict", secondResult.Error.Code);
        Assert.Single(taskCreator.Created); // only one task was created (for the first assignment)
    }

    [Fact]
    public async Task HandleAsync_Publishes_AssetAssigned_Audit_Event_On_Success()
    {
        await using var db = BuildContext();
        var (assetId, companyId) = await SeedAvailableAssetAsync(db);
        var clock = new FakeClock(FixedUtcNow);
        var auditPublisher = new FakeAuditPublisher();
        var handler = new CreateAssetAssignmentHandler(db, clock, new FakeTaskCreator(), new FakeNotificationWriter(), auditPublisher);
        var employeeId = Guid.NewGuid();
        var assignedBy = Guid.NewGuid();

        var result = await handler.HandleAsync(new CreateAssetAssignmentRequest
        {
            CompanyId = companyId,
            AssetId = assetId,
            EmployeeId = employeeId,
            AssignedBy = assignedBy
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.Single(auditPublisher.Published);
        Assert.Equal("asset.assigned", evt.EventType);
        Assert.Equal(result.Value!.Id, evt.EntityId);
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal(assignedBy, evt.ActorUserId);
        Assert.Null(evt.ActorEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_When_Asset_Not_Found()
    {
        await using var db = BuildContext();
        var clock = new FakeClock(FixedUtcNow);
        var auditPublisher = new FakeAuditPublisher();
        var handler = new CreateAssetAssignmentHandler(db, clock, new FakeTaskCreator(), new FakeNotificationWriter(), auditPublisher);

        var result = await handler.HandleAsync(new CreateAssetAssignmentRequest
        {
            CompanyId = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            AssignedBy = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(auditPublisher.Published);
    }
}
