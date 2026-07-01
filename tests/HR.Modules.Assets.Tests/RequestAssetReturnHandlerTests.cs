using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Features.CreateAsset;
using HR.Modules.Assets.Features.CreateAssetAssignment;
using HR.Modules.Assets.Features.CreateAssetCategory;
using HR.Modules.Assets.Features.RequestAssetReturn;
using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

public class RequestAssetReturnHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    private static AssetsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AssetsDbContext(options);
    }

    private static async Task<(Guid assignmentId, Guid employeeId, Guid companyId)> SeedActiveAssignmentAsync(AssetsDbContext db)
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var clock = new FakeClock(FixedUtcNow);

        var categoryResult = await new CreateAssetCategoryHandler(db, clock).HandleAsync(new CreateAssetCategoryRequest
        {
            CompanyId = companyId,
            Name = "Electronics"
        }, CancellationToken.None);

        var assetResult = await new CreateAssetHandler(db, clock).HandleAsync(new CreateAssetRequest
        {
            CompanyId = companyId,
            AssetNumber = "ASSET-001",
            CategoryId = categoryResult.Value!.Id,
            Name = "Laptop"
        }, CancellationToken.None);

        var assignmentResult = await new CreateAssetAssignmentHandler(db, clock, new FakeTaskCreator(), new FakeNotificationWriter()).HandleAsync(
            new CreateAssetAssignmentRequest
            {
                CompanyId = companyId,
                AssetId = assetResult.Value!.Id,
                EmployeeId = employeeId,
                AssignedBy = Guid.NewGuid()
            }, CancellationToken.None);

        return (assignmentResult.Value!.Id, employeeId, companyId);
    }

    [Fact]
    public async Task HandleAsync_Creates_Return_Task_For_Active_Assignment()
    {
        await using var db = BuildContext();
        var (assignmentId, employeeId, companyId) = await SeedActiveAssignmentAsync(db);
        var clock = new FakeClock(FixedUtcNow);
        var taskCreator = new FakeTaskCreator();
        var notificationWriter = new FakeNotificationWriter();
        var requestedBy = Guid.NewGuid();
        var handler = new RequestAssetReturnHandler(db, taskCreator, clock, notificationWriter);

        var result = await handler.HandleAsync(new RequestAssetReturnRequest
        {
            CompanyId = companyId,
            Id = assignmentId,
            RequestedBy = requestedBy
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(taskCreator.Created);
        var task = taskCreator.Created[0];
        Assert.Equal(companyId, task.CompanyId);
        Assert.Equal(requestedBy, task.CreatedBy);
        Assert.Equal("Return asset", task.Title);
        Assert.Equal(TaskPriority.Medium, task.Priority);
        Assert.Equal(TaskSource.Asset, task.Source);
        Assert.Equal(TaskActionType.Return, task.ActionType);
        Assert.Equal(employeeId, task.AssignedEmployeeId);
        Assert.Null(task.AssignedUserId);
        Assert.Equal(assignmentId, task.SourceEntityId);
        Assert.Equal(DateOnly.FromDateTime(FixedUtcNow.AddDays(7)), task.DueDate);

        Assert.Single(notificationWriter.Written);
        var notification = notificationWriter.Written[0];
        Assert.Equal(companyId, notification.CompanyId);
        Assert.Equal(employeeId, notification.EmployeeId);
        Assert.Equal("Asset return requested", notification.Title);
        Assert.Equal(assignmentId, notification.SourceEntityId);
        Assert.Equal(NotificationType.AssetReturnRequested, notification.Type);
        Assert.Equal(NotificationPriority.Normal, notification.Priority);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Assignment_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var clock = new FakeClock(FixedUtcNow);
        var taskCreator = new FakeTaskCreator();
        var handler = new RequestAssetReturnHandler(db, taskCreator, clock, new FakeNotificationWriter());

        var result = await handler.HandleAsync(new RequestAssetReturnRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            RequestedBy = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Assignment_Already_Returned()
    {
        await using var db = BuildContext();
        var (assignmentId, _, companyId) = await SeedActiveAssignmentAsync(db);
        var clock = new FakeClock(FixedUtcNow);

        var assignment = await db.AssetAssignments.FindAsync(assignmentId);
        assignment!.Return(clock.UtcNowOffset());
        await db.SaveChangesAsync();

        var taskCreator = new FakeTaskCreator();
        var handler = new RequestAssetReturnHandler(db, taskCreator, clock, new FakeNotificationWriter());

        var result = await handler.HandleAsync(new RequestAssetReturnRequest
        {
            CompanyId = companyId,
            Id = assignmentId,
            RequestedBy = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Assignment_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var (assignmentId, _, _) = await SeedActiveAssignmentAsync(db);
        var clock = new FakeClock(FixedUtcNow);
        var taskCreator = new FakeTaskCreator();
        var handler = new RequestAssetReturnHandler(db, taskCreator, clock, new FakeNotificationWriter());

        var result = await handler.HandleAsync(new RequestAssetReturnRequest
        {
            CompanyId = Guid.NewGuid(), // different company
            Id = assignmentId,
            RequestedBy = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(taskCreator.Created);
    }
}
