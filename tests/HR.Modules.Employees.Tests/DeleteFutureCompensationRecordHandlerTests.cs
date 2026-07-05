using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.DeleteFutureCompensationRecord;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class DeleteFutureCompensationRecordHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Record_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var publisher = new FakeAuditPublisher();
        var handler = new DeleteFutureCompensationRecordHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Record_Is_Not_Future_Dated()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2020, 1, 1), true, now);
        context.Employees.Add(employee);

        var record = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2026, 6, 8), SalaryType.Annual, 40000m, "GBP", null, null, null, now);
        context.Compensations.Add(record);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new DeleteFutureCompensationRecordHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(companyId, employee.Id, record.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(publisher.Published);

        Assert.NotNull(await context.Compensations.SingleOrDefaultAsync(c => c.Id == record.Id));
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_A_Later_Record_Exists()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2020, 1, 1), true, now);
        context.Employees.Add(employee);

        var earlierFuture = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2027, 1, 1), SalaryType.Annual, 45000m, "GBP", null, null, null, now);
        earlierFuture.Close(new DateOnly(2027, 12, 31), now);
        var laterFuture = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2028, 1, 1), SalaryType.Annual, 50000m, "GBP", null, null, null, now);
        context.Compensations.AddRange(earlierFuture, laterFuture);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new DeleteFutureCompensationRecordHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(companyId, employee.Id, earlierFuture.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(publisher.Published);

        Assert.NotNull(await context.Compensations.SingleOrDefaultAsync(c => c.Id == earlierFuture.Id));
    }

    [Fact]
    public async Task HandleAsync_Deletes_Sole_Open_Future_Record_With_No_Predecessor()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2020, 1, 1), true, now);
        context.Employees.Add(employee);

        var record = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2027, 1, 1), SalaryType.Annual, 45000m, "GBP", null, null, null, now);
        context.Compensations.Add(record);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new DeleteFutureCompensationRecordHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(companyId, employee.Id, record.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await context.Compensations.ToListAsync());

        var deletedEvent = Assert.IsType<CompensationRecordDeletedAuditEvent>(Assert.Single(publisher.Published));
        Assert.Equal(record.Id, deletedEvent.CompensationRecordId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Reopened_Event_When_There_Is_No_Predecessor()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2020, 1, 1), true, now);
        context.Employees.Add(employee);

        var record = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2027, 1, 1), SalaryType.Annual, 45000m, "GBP", null, null, null, now);
        context.Compensations.Add(record);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new DeleteFutureCompensationRecordHandler(context, new FakeClock(FixedUtcNow), publisher);

        await handler.HandleAsync(companyId, employee.Id, record.Id, CancellationToken.None);

        Assert.DoesNotContain(publisher.Published, e => e is CompensationRecordReopenedAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Reopens_Predecessor_When_Deleting_The_Record_That_Closed_It()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2020, 1, 1), true, now);
        context.Employees.Add(employee);

        // Simulates: predecessor was open, then a future record was created effective 2027-01-01,
        // which closed the predecessor on 2026-12-31.
        var predecessor = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2025, 1, 1), SalaryType.Annual, 40000m, "GBP", null, null, null, now);
        predecessor.Close(new DateOnly(2026, 12, 31), now);
        var futureRecord = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2027, 1, 1), SalaryType.Annual, 45000m, "GBP", null, null, null, now);
        context.Compensations.AddRange(predecessor, futureRecord);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new DeleteFutureCompensationRecordHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(companyId, employee.Id, futureRecord.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var remaining = await context.Compensations.SingleAsync();
        Assert.Equal(predecessor.Id, remaining.Id);
        Assert.Null(remaining.EffectiveTo);

        var reopenedEvent = Assert.IsType<CompensationRecordReopenedAuditEvent>(
            Assert.Single(publisher.Published, e => e is CompensationRecordReopenedAuditEvent));
        Assert.Equal(predecessor.Id, reopenedEvent.CompensationRecordId);
        Assert.Equal(new DateOnly(2026, 12, 31), reopenedEvent.PreviousEffectiveTo);

        Assert.Contains(publisher.Published, e => e is CompensationRecordDeletedAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Delete_Record_Belonging_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2020, 1, 1), true, now);
        context.Employees.Add(employee);

        var record = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2027, 1, 1), SalaryType.Annual, 45000m, "GBP", null, null, null, now);
        context.Compensations.Add(record);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new DeleteFutureCompensationRecordHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(otherCompanyId, employee.Id, record.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.NotNull(await context.Compensations.SingleOrDefaultAsync(c => c.Id == record.Id));
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
