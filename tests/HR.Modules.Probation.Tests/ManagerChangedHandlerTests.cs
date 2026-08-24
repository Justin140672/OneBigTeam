using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.ReassignReviewsOnManagerChanged;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.Modules.Tasks.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Probation.Tests;

public class ManagerChangedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly ExpectedEndDate = new(2026, 4, 1);

    [Fact]
    public async Task HandleAsync_With_Pending_ManagerCheckIn_Cancels_Old_Task_And_Creates_New_Task_For_New_Manager()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var oldManagerId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();

        var (record, review) = await SeedRecordWithPendingCheckIn(context, companyId, employeeId, oldManagerId);

        var taskCreator = new FakeTaskCreator();
        var taskCanceller = new FakeTaskCanceller();
        var handler = BuildHandler(context, taskCreator, taskCanceller);

        await handler.HandleAsync(
            new EmployeeManagerChangedIntegrationEvent(companyId, employeeId, oldManagerId, newManagerId, Now),
            CancellationToken.None);

        var cancelCall = Assert.Single(taskCanceller.Calls);
        Assert.Equal(review.Id, cancelCall.SourceEntityId);

        var createdTask = Assert.Single(taskCreator.Created);
        Assert.Equal(newManagerId, createdTask.AssignedEmployeeId);
        Assert.Equal(newManagerId, createdTask.AssignedUserId);
        Assert.Equal(review.Id, createdTask.SourceEntityId);

        var updatedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(newManagerId, updatedRecord.ManagerEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_With_No_Pending_ManagerCheckIn_Updates_Manager_Without_Task_Calls()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var oldManagerId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, oldManagerId, StartDate, ExpectedEndDate, null, Now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var taskCreator = new FakeTaskCreator();
        var taskCanceller = new FakeTaskCanceller();
        var handler = BuildHandler(context, taskCreator, taskCanceller);

        await handler.HandleAsync(
            new EmployeeManagerChangedIntegrationEvent(companyId, employeeId, oldManagerId, newManagerId, Now),
            CancellationToken.None);

        Assert.Empty(taskCanceller.Calls);
        Assert.Empty(taskCreator.Created);

        var updatedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(newManagerId, updatedRecord.ManagerEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_With_Null_NewManagerId_Cancels_Task_And_Leaves_Manager_Unchanged()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var oldManagerId = Guid.NewGuid();

        var (record, review) = await SeedRecordWithPendingCheckIn(context, companyId, employeeId, oldManagerId);

        var taskCreator = new FakeTaskCreator();
        var taskCanceller = new FakeTaskCanceller();
        var handler = BuildHandler(context, taskCreator, taskCanceller);

        var exception = await Record.ExceptionAsync(() => handler.HandleAsync(
            new EmployeeManagerChangedIntegrationEvent(companyId, employeeId, oldManagerId, null, Now),
            CancellationToken.None));

        Assert.Null(exception);

        var cancelCall = Assert.Single(taskCanceller.Calls);
        Assert.Equal(review.Id, cancelCall.SourceEntityId);
        Assert.Empty(taskCreator.Created);

        var updatedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(oldManagerId, updatedRecord.ManagerEmployeeId);
        Assert.NotEqual(Guid.Empty, updatedRecord.ManagerEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Duplicate_Event_Delivery_Is_A_No_Op()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();

        // Manager already equals NewManagerId — simulating that the first delivery already applied.
        var (record, review) = await SeedRecordWithPendingCheckIn(context, companyId, employeeId, newManagerId);

        var taskCreator = new FakeTaskCreator();
        var taskCanceller = new FakeTaskCanceller();
        var handler = BuildHandler(context, taskCreator, taskCanceller);

        await handler.HandleAsync(
            new EmployeeManagerChangedIntegrationEvent(companyId, employeeId, Guid.NewGuid(), newManagerId, Now),
            CancellationToken.None);

        Assert.Empty(taskCanceller.Calls);
        Assert.Empty(taskCreator.Created);

        var reloadedReview = await context.ProbationReviews.SingleAsync(r => r.Id == review.Id);
        Assert.Equal(ProbationReviewStatus.Pending, reloadedReview.Status);
    }

    [Fact]
    public async Task HandleAsync_Second_Call_With_Same_NewManagerId_Produces_No_Additional_Calls()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var oldManagerId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();

        await SeedRecordWithPendingCheckIn(context, companyId, employeeId, oldManagerId);

        var taskCreator = new FakeTaskCreator();
        var taskCanceller = new FakeTaskCanceller();

        var firstEvent = new EmployeeManagerChangedIntegrationEvent(companyId, employeeId, oldManagerId, newManagerId, Now);

        await BuildHandler(context, taskCreator, taskCanceller).HandleAsync(firstEvent, CancellationToken.None);

        var callsAfterFirst = taskCanceller.Calls.Count;
        var createdAfterFirst = taskCreator.Created.Count;

        // Redelivery of the same event.
        await BuildHandler(context, taskCreator, taskCanceller).HandleAsync(firstEvent, CancellationToken.None);

        Assert.Equal(callsAfterFirst, taskCanceller.Calls.Count);
        Assert.Equal(createdAfterFirst, taskCreator.Created.Count);
    }

    [Fact]
    public async Task HandleAsync_No_Probation_Record_Is_A_No_Op()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var taskCreator = new FakeTaskCreator();
        var taskCanceller = new FakeTaskCanceller();
        var handler = BuildHandler(context, taskCreator, taskCanceller);

        var exception = await Record.ExceptionAsync(() => handler.HandleAsync(
            new EmployeeManagerChangedIntegrationEvent(companyId, employeeId, Guid.NewGuid(), Guid.NewGuid(), Now),
            CancellationToken.None));

        Assert.Null(exception);
        Assert.Empty(taskCanceller.Calls);
        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task HandleAsync_Completed_Probation_Record_Is_A_No_Op()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var oldManagerId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, oldManagerId, StartDate, ExpectedEndDate, null, Now);
        record.Pass(Guid.NewGuid(), ExpectedEndDate, null, Now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var taskCreator = new FakeTaskCreator();
        var taskCanceller = new FakeTaskCanceller();
        var handler = BuildHandler(context, taskCreator, taskCanceller);

        await handler.HandleAsync(
            new EmployeeManagerChangedIntegrationEvent(companyId, employeeId, oldManagerId, newManagerId, Now),
            CancellationToken.None);

        Assert.Empty(taskCanceller.Calls);
        Assert.Empty(taskCreator.Created);

        var reloadedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(oldManagerId, reloadedRecord.ManagerEmployeeId);
    }

    private static async Task<(ProbationRecord Record, ProbationReview Review)> SeedRecordWithPendingCheckIn(
        ProbationDbContext context, Guid companyId, Guid employeeId, Guid managerId)
    {
        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, managerId, StartDate, ExpectedEndDate, null, Now);
        context.ProbationRecords.Add(record);

        var review = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, ProbationReviewType.ManagerCheckIn,
            StartDate.AddDays(30), Now);
        context.ProbationReviews.Add(review);

        await context.SaveChangesAsync();
        return (record, review);
    }

    private static ManagerChangedHandler BuildHandler(
        ProbationDbContext context, FakeTaskCreator taskCreator, FakeTaskCanceller taskCanceller) =>
        new(context,
            taskCreator,
            taskCanceller,
            new FakeEmployeeNameReader(),
            new FakeClock(FixedUtcNow),
            NullLogger<ManagerChangedHandler>.Instance);

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
