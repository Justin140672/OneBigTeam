using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateFutureCompensationRecord;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class UpdateFutureCompensationRecordHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Record_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var publisher = new FakeAuditPublisher();
        var handler = new UpdateFutureCompensationRecordHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(
            new UpdateFutureCompensationRecordRequest
            {
                CompanyId = Guid.NewGuid(),
                EmployeeId = Guid.NewGuid(),
                Id = Guid.NewGuid(),
                SalaryType = SalaryType.Annual,
                Salary = 45000m,
                Currency = "GBP"
            },
            CancellationToken.None);

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
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, now);
        context.Employees.Add(employee);

        // EffectiveFrom == today (2026-06-08) — already in effect, not future.
        var record = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2026, 6, 8), SalaryType.Annual, 40000m, "GBP", null, null, null, now);
        context.Compensations.Add(record);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new UpdateFutureCompensationRecordHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(
            new UpdateFutureCompensationRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employee.Id,
                Id = record.Id,
                SalaryType = SalaryType.Annual,
                Salary = 41000m,
                Currency = "GBP"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(publisher.Published);

        var unchanged = await context.Compensations.SingleAsync();
        Assert.Equal(40000m, unchanged.Salary);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Record_Is_In_The_Past()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2020, 1, 1), true, now);
        context.Employees.Add(employee);

        var record = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2024, 1, 1), SalaryType.Annual, 35000m, "GBP", null, null, null, now);
        context.Compensations.Add(record);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new UpdateFutureCompensationRecordHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(
            new UpdateFutureCompensationRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employee.Id,
                Id = record.Id,
                SalaryType = SalaryType.Annual,
                Salary = 36000m,
                Currency = "GBP"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Updates_Future_Dated_Record()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2020, 1, 1), true, now);
        context.Employees.Add(employee);

        var record = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2027, 1, 1), SalaryType.Annual, 45000m, "GBP", 37.5m, 1m, "Original", now);
        context.Compensations.Add(record);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new UpdateFutureCompensationRecordHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(
            new UpdateFutureCompensationRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employee.Id,
                Id = record.Id,
                SalaryType = SalaryType.Hourly,
                Salary = 25m,
                Currency = " usd ",
                HoursPerWeek = 20m,
                FTE = 0.5m,
                Notes = "  Corrected  "
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hourly", result.Value!.SalaryType);
        Assert.Equal(25m, result.Value.Salary);
        Assert.Equal("USD", result.Value.Currency);
        Assert.Equal(20m, result.Value.HoursPerWeek);
        Assert.Equal(0.5m, result.Value.FTE);
        Assert.Equal("Corrected", result.Value.Notes);
        // EffectiveFrom is not editable through this slice.
        Assert.Equal(new DateOnly(2027, 1, 1), result.Value.EffectiveFrom);

        var saved = await context.Compensations.SingleAsync();
        Assert.Equal(25m, saved.Salary);

        var updatedEvent = Assert.IsType<CompensationRecordUpdatedAuditEvent>(Assert.Single(publisher.Published));
        Assert.Equal(record.Id, updatedEvent.CompensationRecordId);
        Assert.Equal("Hourly", updatedEvent.SalaryType);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Update_Record_Belonging_To_Different_Employee()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2020, 1, 1), true, now);
        var otherEmployee = Employee.Create(Guid.NewGuid(), companyId, "Bob", "Jones", "bob@example.com", new DateOnly(2020, 1, 1), true, now);
        context.Employees.AddRange(employee, otherEmployee);

        var record = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2027, 1, 1), SalaryType.Annual, 45000m, "GBP", null, null, null, now);
        context.Compensations.Add(record);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new UpdateFutureCompensationRecordHandler(context, new FakeClock(FixedUtcNow), publisher);

        var result = await handler.HandleAsync(
            new UpdateFutureCompensationRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = otherEmployee.Id,
                Id = record.Id,
                SalaryType = SalaryType.Annual,
                Salary = 99999m,
                Currency = "GBP"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
