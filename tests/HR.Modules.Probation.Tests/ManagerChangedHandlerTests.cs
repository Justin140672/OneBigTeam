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
            Guid.NewGuid(), companyId, employeeId, oldManagerId, StartDate, ExpectedEndDate, null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
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
            Guid.NewGuid(), companyId, employeeId, oldManagerId, StartDate, ExpectedEndDate, null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
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

    [Fact]
    public async Task HandleAsync_No_Record_Exists_With_ProbationDates_Available_Creates_Deferred_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();
        var probationStartDate = new DateOnly(2026, 1, 1);
        var probationEndDate = new DateOnly(2026, 4, 1);

        var probationDatesReader = new FakeEmployeeProbationDatesReader(
            new EmployeeProbationDates(probationStartDate, probationEndDate));

        var taskCreator = new FakeTaskCreator();
        var taskCanceller = new FakeTaskCanceller();
        var handler = BuildHandler(context, taskCreator, taskCanceller, probationDatesReader);

        await handler.HandleAsync(
            new EmployeeManagerChangedIntegrationEvent(companyId, employeeId, Guid.NewGuid(), newManagerId, Now),
            CancellationToken.None);

        var created = await context.ProbationRecords.SingleAsync();
        Assert.Equal(companyId, created.CompanyId);
        Assert.Equal(employeeId, created.EmployeeId);
        Assert.Equal(newManagerId, created.ManagerEmployeeId);
        Assert.Equal(probationStartDate, created.StartDate);
        Assert.Equal(probationEndDate, created.ExpectedEndDate);
        // FixedUtcNow (2026-06-25) is after probationStartDate, so the record starts Active.
        Assert.Equal(ProbationStatus.Active, created.Status);
    }

    [Fact]
    public async Task HandleAsync_Deferred_Record_Creation_Publishes_Audit_Event_With_System_Actor()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();
        var probationStartDate = new DateOnly(2026, 1, 1);
        var probationEndDate = new DateOnly(2026, 4, 1);

        var probationDatesReader = new FakeEmployeeProbationDatesReader(
            new EmployeeProbationDates(probationStartDate, probationEndDate));

        var taskCreator = new FakeTaskCreator();
        var taskCanceller = new FakeTaskCanceller();
        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, taskCreator, taskCanceller, probationDatesReader, auditPublisher);

        await handler.HandleAsync(
            new EmployeeManagerChangedIntegrationEvent(companyId, employeeId, Guid.NewGuid(), newManagerId, Now),
            CancellationToken.None);

        var evt = Assert.IsType<ProbationRecordCreatedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(ProbationSystemActor.Id, evt.ActorEmployeeIdValue);
        Assert.NotEqual(employeeId, evt.ActorEmployeeIdValue);
        Assert.NotEqual(newManagerId, evt.ActorEmployeeIdValue);
        Assert.False(evt.HasNotes);
    }

    [Fact]
    public async Task HandleAsync_No_Record_Exists_With_Future_StartDate_Creates_Deferred_NotStarted_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();
        var futureStartDate = DateOnly.FromDateTime(FixedUtcNow).AddDays(10);
        var futureEndDate = futureStartDate.AddMonths(3);

        var probationDatesReader = new FakeEmployeeProbationDatesReader(
            new EmployeeProbationDates(futureStartDate, futureEndDate));

        var taskCreator = new FakeTaskCreator();
        var taskCanceller = new FakeTaskCanceller();
        var handler = BuildHandler(context, taskCreator, taskCanceller, probationDatesReader);

        await handler.HandleAsync(
            new EmployeeManagerChangedIntegrationEvent(companyId, employeeId, Guid.NewGuid(), newManagerId, Now),
            CancellationToken.None);

        var created = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.NotStarted, created.Status);
    }

    [Fact]
    public async Task HandleAsync_No_Record_Exists_And_ProbationDatesReader_Returns_Null_Creates_No_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();

        var probationDatesReader = new FakeEmployeeProbationDatesReader(dates: null);

        var taskCreator = new FakeTaskCreator();
        var taskCanceller = new FakeTaskCanceller();
        var handler = BuildHandler(context, taskCreator, taskCanceller, probationDatesReader);

        await handler.HandleAsync(
            new EmployeeManagerChangedIntegrationEvent(companyId, employeeId, Guid.NewGuid(), newManagerId, Now),
            CancellationToken.None);

        Assert.Equal(0, await context.ProbationRecords.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_No_Record_Exists_And_ProbationDatesReader_Returns_Null_ProbationEndDate_Creates_No_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();

        var probationDatesReader = new FakeEmployeeProbationDatesReader(
            new EmployeeProbationDates(new DateOnly(2026, 1, 1), null));

        var taskCreator = new FakeTaskCreator();
        var taskCanceller = new FakeTaskCanceller();
        var handler = BuildHandler(context, taskCreator, taskCanceller, probationDatesReader);

        await handler.HandleAsync(
            new EmployeeManagerChangedIntegrationEvent(companyId, employeeId, Guid.NewGuid(), newManagerId, Now),
            CancellationToken.None);

        Assert.Equal(0, await context.ProbationRecords.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_No_Record_Exists_And_NewManagerId_Null_Creates_No_Deferred_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var probationDatesReader = new FakeEmployeeProbationDatesReader(
            new EmployeeProbationDates(new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1)));

        var taskCreator = new FakeTaskCreator();
        var taskCanceller = new FakeTaskCanceller();
        var handler = BuildHandler(context, taskCreator, taskCanceller, probationDatesReader);

        await handler.HandleAsync(
            new EmployeeManagerChangedIntegrationEvent(companyId, employeeId, Guid.NewGuid(), null, Now),
            CancellationToken.None);

        Assert.Equal(0, await context.ProbationRecords.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_Record_Already_Exists_As_NotApplicable_Is_Never_Touched_By_Deferred_Creation()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var oldManagerId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();

        var record = ProbationRecord.CreateNotApplicable(
            Guid.NewGuid(), companyId, employeeId, oldManagerId, StartDate, ExpectedEndDate, "Exempt.", Now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var probationDatesReader = new FakeEmployeeProbationDatesReader(
            new EmployeeProbationDates(StartDate, ExpectedEndDate));

        var taskCreator = new FakeTaskCreator();
        var taskCanceller = new FakeTaskCanceller();
        var handler = BuildHandler(context, taskCreator, taskCanceller, probationDatesReader);

        await handler.HandleAsync(
            new EmployeeManagerChangedIntegrationEvent(companyId, employeeId, oldManagerId, newManagerId, Now),
            CancellationToken.None);

        Assert.Equal(1, await context.ProbationRecords.CountAsync());
        var reloaded = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.NotApplicable, reloaded.Status);
        Assert.Equal(oldManagerId, reloaded.ManagerEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_NotStarted_Record_Has_Manager_Updated_Via_In_Flight_Status_Filter()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var oldManagerId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();
        var futureStartDate = DateOnly.FromDateTime(FixedUtcNow).AddDays(10);
        var futureEndDate = futureStartDate.AddMonths(3);

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, oldManagerId, futureStartDate, futureEndDate, null,
            DateOnly.FromDateTime(FixedUtcNow).AddDays(-1), Now);
        Assert.Equal(ProbationStatus.NotStarted, record.Status);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var taskCreator = new FakeTaskCreator();
        var taskCanceller = new FakeTaskCanceller();
        var handler = BuildHandler(context, taskCreator, taskCanceller);

        await handler.HandleAsync(
            new EmployeeManagerChangedIntegrationEvent(companyId, employeeId, oldManagerId, newManagerId, Now),
            CancellationToken.None);

        var updatedRecord = await context.ProbationRecords.SingleAsync();
        Assert.Equal(newManagerId, updatedRecord.ManagerEmployeeId);
        Assert.Equal(ProbationStatus.NotStarted, updatedRecord.Status);
    }

    private static async Task<(ProbationRecord Record, ProbationReview Review)> SeedRecordWithPendingCheckIn(
        ProbationDbContext context, Guid companyId, Guid employeeId, Guid managerId)
    {
        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, managerId, StartDate, ExpectedEndDate, null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
        context.ProbationRecords.Add(record);

        var review = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, ProbationReviewType.ManagerCheckIn,
            StartDate.AddDays(30), Now);
        context.ProbationReviews.Add(review);

        await context.SaveChangesAsync();
        return (record, review);
    }

    private static ManagerChangedHandler BuildHandler(
        ProbationDbContext context,
        FakeTaskCreator taskCreator,
        FakeTaskCanceller taskCanceller,
        IEmployeeProbationDatesReader? probationDatesReader = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(context,
            taskCreator,
            taskCanceller,
            new FakeEmployeeNameReader(),
            probationDatesReader ?? new FakeEmployeeProbationDatesReader(),
            new FakeCompanyTimeZoneReader(),
            new FakeClock(FixedUtcNow),
            auditPublisher ?? new FakeAuditPublisher(),
            NullLogger<ManagerChangedHandler>.Instance);

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
