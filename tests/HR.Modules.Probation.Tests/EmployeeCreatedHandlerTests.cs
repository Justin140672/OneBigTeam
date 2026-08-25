using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.CreateProbationOnEmployeeCreated;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class EmployeeCreatedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Creates_ProbationRecord_When_Manager_Is_Present()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 1, 1);
        var probationEndDate = new DateOnly(2026, 7, 1);

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyTimeZoneReader(), new FakeAuditPublisher());

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, employeeId, startDate, managerId, probationEndDate),
            CancellationToken.None);

        var record = await context.ProbationRecords.SingleAsync();
        Assert.Equal(companyId, record.CompanyId);
        Assert.Equal(employeeId, record.EmployeeId);
        Assert.Equal(managerId, record.ManagerEmployeeId);
        Assert.Equal(startDate, record.StartDate);
        Assert.Equal(probationEndDate, record.ExpectedEndDate);
        Assert.Equal(ProbationStatus.Active, record.Status);
        Assert.Null(record.Notes);
        Assert.Equal(Now, record.CreatedAt);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event_With_System_Actor()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 1, 1);
        var probationEndDate = new DateOnly(2026, 7, 1);

        var publisher = new FakeAuditPublisher();
        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyTimeZoneReader(), publisher);

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, employeeId, startDate, managerId, probationEndDate),
            CancellationToken.None);

        var evt = Assert.IsType<ProbationRecordCreatedAuditEvent>(Assert.Single(publisher.Published));
        Assert.Equal(ProbationSystemActor.Id, evt.ActorEmployeeIdValue);
        Assert.NotNull(evt.ActorEmployeeIdValue);
        Assert.NotEqual(employeeId, evt.ActorEmployeeIdValue);
        Assert.False(evt.HasNotes);
    }

    [Fact]
    public async Task HandleAsync_Uses_ProbationEndDate_From_Event_As_ExpectedEndDate()
    {
        await using var context = BuildContext();
        var startDate = new DateOnly(2026, 1, 1);
        var probationEndDate = new DateOnly(2026, 10, 15);

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyTimeZoneReader(), new FakeAuditPublisher());

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), startDate, Guid.NewGuid(), probationEndDate),
            CancellationToken.None);

        var record = await context.ProbationRecords.SingleAsync();
        Assert.Equal(probationEndDate, record.ExpectedEndDate);
    }

    [Fact]
    public async Task HandleAsync_Does_Nothing_When_Manager_Is_Null()
    {
        await using var context = BuildContext();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyTimeZoneReader(), new FakeAuditPublisher());

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), ManagerId: null, new DateOnly(2027, 1, 1)),
            CancellationToken.None);

        Assert.Equal(0, await context.ProbationRecords.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_Creates_Separate_Records_For_Different_Employees()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 7, 1);
        var probationEndDate = new DateOnly(2027, 1, 1);

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyTimeZoneReader(), new FakeAuditPublisher());

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, Guid.NewGuid(), startDate, managerId, probationEndDate),
            CancellationToken.None);

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, Guid.NewGuid(), startDate, managerId, probationEndDate),
            CancellationToken.None);

        Assert.Equal(2, await context.ProbationRecords.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_Duplicate_Delivery_When_Record_Already_Exists_In_Any_Status_Is_A_NoOp()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 1, 1);
        var probationEndDate = new DateOnly(2026, 4, 1);
        var now = Now;

        var existing = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, managerId,
            startDate, probationEndDate, null, DateOnly.FromDateTime(now.UtcDateTime), now);
        existing.Pass(managerId, probationEndDate, null, now);
        context.ProbationRecords.Add(existing);
        await context.SaveChangesAsync();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyTimeZoneReader(), new FakeAuditPublisher());

        var exception = await Record.ExceptionAsync(() => handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, employeeId, startDate, managerId, probationEndDate),
            CancellationToken.None));

        Assert.Null(exception);
        Assert.Equal(1, await context.ProbationRecords.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_With_Future_StartDate_Creates_NotStarted_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var startDate = DateOnly.FromDateTime(FixedUtcNow).AddDays(10);
        var probationEndDate = startDate.AddMonths(6);

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyTimeZoneReader(), new FakeAuditPublisher());

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, employeeId, startDate, managerId, probationEndDate),
            CancellationToken.None);

        var record = await context.ProbationRecords.SingleAsync();
        Assert.Equal(ProbationStatus.NotStarted, record.Status);
    }

    [Fact]
    public async Task HandleAsync_With_No_Manager_And_Not_Imported_Creates_No_Record()
    {
        await using var context = BuildContext();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyTimeZoneReader(), new FakeAuditPublisher());

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), ManagerId: null, new DateOnly(2027, 1, 1),
                IsImported: false),
            CancellationToken.None);

        Assert.Equal(0, await context.ProbationRecords.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_Imported_Employee_With_Manager_Creates_No_Record()
    {
        await using var context = BuildContext();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyTimeZoneReader(), new FakeAuditPublisher());

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), Guid.NewGuid(), new DateOnly(2027, 1, 1),
                IsImported: true),
            CancellationToken.None);

        Assert.Equal(0, await context.ProbationRecords.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_Imported_Employee_Without_Manager_Creates_No_Record()
    {
        await using var context = BuildContext();

        var handler = new EmployeeCreatedHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyTimeZoneReader(), new FakeAuditPublisher());

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), ManagerId: null, new DateOnly(2027, 1, 1),
                IsImported: true),
            CancellationToken.None);

        Assert.Equal(0, await context.ProbationRecords.CountAsync());
    }

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
