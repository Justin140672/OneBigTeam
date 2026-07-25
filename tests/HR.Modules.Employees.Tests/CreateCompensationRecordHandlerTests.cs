using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateCompensationRecord;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class CreateCompensationRecordHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ActorEmployeeId = Guid.NewGuid();

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var publisher = new FakeAuditPublisher();
        var handler = new CreateCompensationRecordHandler(new CompensationRecordWriter(context, new FakeClock(FixedUtcNow)), publisher);

        var result = await handler.HandleAsync(
            new CreateCompensationRecordRequest
            {
                CompanyId = Guid.NewGuid(),
                EmployeeId = Guid.NewGuid(),
                EffectiveFrom = new DateOnly(2026, 1, 1),
                SalaryType = SalaryType.Annual,
                Salary = 45000m,
                Currency = "GBP",
                Reason = CompensationChangeReason.NewHire
            },
            ActorEmployeeId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Creates_First_Record_With_No_Previous_To_Close()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new CreateCompensationRecordHandler(new CompensationRecordWriter(context, new FakeClock(FixedUtcNow)), publisher);

        var result = await handler.HandleAsync(
            new CreateCompensationRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employee.Id,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                SalaryType = SalaryType.Annual,
                Salary = 45000m,
                Currency = " gbp ",
                HoursPerWeek = 37.5m,
                FTE = 1m,
                Notes = "  Starting salary  ",
                Reason = CompensationChangeReason.NewHire
            },
            ActorEmployeeId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.EffectiveTo);
        Assert.Equal("GBP", result.Value.Currency);
        Assert.Equal("Starting salary", result.Value.Notes);
        Assert.Equal("NewHire", result.Value.Reason);
        Assert.Equal(ActorEmployeeId, result.Value.CreatedBy);

        var saved = await context.Compensations.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
        Assert.Null(saved.EffectiveTo);

        var createdEvent = Assert.IsType<CompensationRecordCreatedAuditEvent>(Assert.Single(publisher.Published));
        Assert.Equal(companyId, createdEvent.CompanyId);
        Assert.Equal(employee.Id, createdEvent.EmployeeId);
        Assert.Equal(saved.Id, createdEvent.CompensationRecordId);
        Assert.Equal(ActorEmployeeId, createdEvent.ActorEmployeeId);
        Assert.Equal("NewHire", createdEvent.Reason);

        // The IAuditEvent.EmployeeId interface member must round-trip to the subject employee's ID —
        // this is what lets the audit history reader find "all events belonging to employee X".
        Assert.Equal(employee.Id, ((HR.SharedKernel.IAuditEvent)createdEvent).EmployeeId);
        Assert.Equal(ActorEmployeeId, ((HR.SharedKernel.IAuditEvent)createdEvent).ActorEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Closes_Previous_Open_Record_When_Creating_New_One()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);

        var existing = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2025, 1, 1), SalaryType.Annual, 40000m, "GBP", 37.5m, 1m, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        context.Compensations.Add(existing);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new CreateCompensationRecordHandler(new CompensationRecordWriter(context, new FakeClock(FixedUtcNow)), publisher);

        var result = await handler.HandleAsync(
            new CreateCompensationRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employee.Id,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                SalaryType = SalaryType.Annual,
                Salary = 45000m,
                Currency = "GBP",
                Reason = CompensationChangeReason.AnnualReview
            },
            ActorEmployeeId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var previousReloaded = await context.Compensations.SingleAsync(c => c.Id == existing.Id);
        Assert.Equal(new DateOnly(2025, 12, 31), previousReloaded.EffectiveTo);

        Assert.Equal(2, context.Compensations.Local.Count);
        Assert.Equal(2, publisher.Published.Count);

        var closedEvent = Assert.IsType<CompensationRecordClosedAuditEvent>(publisher.Published[0]);
        Assert.Equal(existing.Id, closedEvent.CompensationRecordId);
        Assert.Equal(new DateOnly(2025, 12, 31), closedEvent.EffectiveTo);
        Assert.Equal(ActorEmployeeId, closedEvent.ActorEmployeeId);

        var createdEvent = Assert.IsType<CompensationRecordCreatedAuditEvent>(publisher.Published[1]);
        Assert.Equal(result.Value!.Id, createdEvent.CompensationRecordId);
        Assert.Equal(ActorEmployeeId, createdEvent.ActorEmployeeId);
        Assert.Equal("AnnualReview", createdEvent.Reason);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_EffectiveFrom_Not_After_Open_Record()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);

        var existing = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2026, 1, 1), SalaryType.Annual, 40000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        context.Compensations.Add(existing);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new CreateCompensationRecordHandler(new CompensationRecordWriter(context, new FakeClock(FixedUtcNow)), publisher);

        var result = await handler.HandleAsync(
            new CreateCompensationRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employee.Id,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                SalaryType = SalaryType.Annual,
                Salary = 45000m,
                Currency = "GBP",
                Reason = CompensationChangeReason.AnnualReview
            },
            ActorEmployeeId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(publisher.Published);

        var unchangedExisting = await context.Compensations.SingleAsync(c => c.Id == existing.Id);
        Assert.Null(unchangedExisting.EffectiveTo);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_EffectiveFrom_Falls_Inside_Closed_Historical_Record()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2020, 1, 1), true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);

        var historical = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2024, 1, 1), SalaryType.Annual, 35000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        historical.Close(new DateOnly(2024, 12, 31), now);
        var open = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2025, 1, 1), SalaryType.Annual, 40000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        context.Compensations.AddRange(historical, open);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new CreateCompensationRecordHandler(new CompensationRecordWriter(context, new FakeClock(FixedUtcNow)), publisher);

        // Backdated into the middle of the already-closed 2024 record — must be rejected even though
        // it's chronologically before the currently open record.
        var result = await handler.HandleAsync(
            new CreateCompensationRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employee.Id,
                EffectiveFrom = new DateOnly(2024, 6, 1),
                SalaryType = SalaryType.Annual,
                Salary = 36000m,
                Currency = "GBP",
                Reason = CompensationChangeReason.Correction
            },
            ActorEmployeeId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(publisher.Published);

        var unchangedHistorical = await context.Compensations.SingleAsync(c => c.Id == historical.Id);
        Assert.Equal(new DateOnly(2024, 12, 31), unchangedHistorical.EffectiveTo);
        var unchangedOpen = await context.Compensations.SingleAsync(c => c.Id == open.Id);
        Assert.Null(unchangedOpen.EffectiveTo);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_EffectiveFrom_Equals_Closed_Record_EffectiveTo()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2020, 1, 1), true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);

        var historical = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2024, 1, 1), SalaryType.Annual, 35000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        historical.Close(new DateOnly(2024, 12, 31), now);
        context.Compensations.Add(historical);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new CreateCompensationRecordHandler(new CompensationRecordWriter(context, new FakeClock(FixedUtcNow)), publisher);

        var result = await handler.HandleAsync(
            new CreateCompensationRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employee.Id,
                EffectiveFrom = new DateOnly(2024, 12, 31),
                SalaryType = SalaryType.Annual,
                Salary = 36000m,
                Currency = "GBP",
                Reason = CompensationChangeReason.Correction
            },
            ActorEmployeeId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_EffectiveFrom_Exactly_Matches_Existing_Open_Record_EffectiveFrom()
    {
        // Exact-duplicate-date case: new EffectiveFrom equal to an existing open record's EffectiveFrom.
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2020, 1, 1), true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);

        var existing = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2026, 3, 1), SalaryType.Annual, 40000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        context.Compensations.Add(existing);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new CreateCompensationRecordHandler(new CompensationRecordWriter(context, new FakeClock(FixedUtcNow)), publisher);

        var result = await handler.HandleAsync(
            new CreateCompensationRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employee.Id,
                EffectiveFrom = new DateOnly(2026, 3, 1),
                SalaryType = SalaryType.Annual,
                Salary = 45000m,
                Currency = "GBP",
                Reason = CompensationChangeReason.Correction
            },
            ActorEmployeeId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(publisher.Published);

        var unchanged = await context.Compensations.SingleAsync(c => c.Id == existing.Id);
        Assert.Null(unchanged.EffectiveTo);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_EffectiveFrom_Is_Day_After_Closed_Record_EffectiveTo()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2020, 1, 1), true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);

        var historical = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2024, 1, 1), SalaryType.Annual, 35000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        historical.Close(new DateOnly(2024, 12, 31), now);
        context.Compensations.Add(historical);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new CreateCompensationRecordHandler(new CompensationRecordWriter(context, new FakeClock(FixedUtcNow)), publisher);

        var result = await handler.HandleAsync(
            new CreateCompensationRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employee.Id,
                EffectiveFrom = new DateOnly(2025, 1, 1),
                SalaryType = SalaryType.Annual,
                Salary = 36000m,
                Currency = "GBP",
                Reason = CompensationChangeReason.Correction
            },
            ActorEmployeeId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_New_Open_Ended_Record_Would_Swallow_A_Future_Historical_Record()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2020, 1, 1), true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);

        // A closed record entirely in the future relative to the requested EffectiveFrom.
        var futureHistorical = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2027, 1, 1), SalaryType.Annual, 50000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        futureHistorical.Close(new DateOnly(2027, 12, 31), now);
        context.Compensations.Add(futureHistorical);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new CreateCompensationRecordHandler(new CompensationRecordWriter(context, new FakeClock(FixedUtcNow)), publisher);

        var result = await handler.HandleAsync(
            new CreateCompensationRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employee.Id,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                SalaryType = SalaryType.Annual,
                Salary = 45000m,
                Currency = "GBP",
                Reason = CompensationChangeReason.AnnualReview
            },
            ActorEmployeeId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Close_Already_Closed_Records()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);

        var alreadyClosed = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2024, 1, 1), SalaryType.Annual, 30000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        alreadyClosed.Close(new DateOnly(2024, 12, 31), now);
        context.Compensations.Add(alreadyClosed);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = new CreateCompensationRecordHandler(new CompensationRecordWriter(context, new FakeClock(FixedUtcNow)), publisher);

        var result = await handler.HandleAsync(
            new CreateCompensationRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employee.Id,
                EffectiveFrom = new DateOnly(2025, 1, 1),
                SalaryType = SalaryType.Annual,
                Salary = 35000m,
                Currency = "GBP",
                Reason = CompensationChangeReason.AnnualReview
            },
            ActorEmployeeId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Only the CompensationRecordCreatedAuditEvent should fire — nothing was open to close.
        var singleEvent = Assert.Single(publisher.Published);
        Assert.IsType<CompensationRecordCreatedAuditEvent>(singleEvent);

        var stillClosed = await context.Compensations.SingleAsync(c => c.Id == alreadyClosed.Id);
        Assert.Equal(new DateOnly(2024, 12, 31), stillClosed.EffectiveTo);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
