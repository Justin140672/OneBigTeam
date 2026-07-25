using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HR.Modules.Employees.Tests;

public class CompensationRecordWriterTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public async Task WriteAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var writer = new CompensationRecordWriter(context, new FakeClock(FixedUtcNow));

        var result = await writer.WriteAsync(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), SalaryType.Annual, 45000m,
            "GBP", null, null, null, CompensationChangeReason.NewHire, ActorId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task WriteAsync_Creates_Record_When_No_Existing_Records()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var writer = new CompensationRecordWriter(context, new FakeClock(FixedUtcNow));

        var result = await writer.WriteAsync(
            companyId, employee.Id, new DateOnly(2026, 1, 1), SalaryType.Annual, 45000m,
            " gbp ", 37.5m, 1m, "  Notes  ", CompensationChangeReason.NewHire, ActorId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ClosedPrevious);
        Assert.Equal("GBP", result.Value.Created.Currency);
        Assert.Equal("Notes", result.Value.Created.Notes);

        var saved = await context.Compensations.SingleAsync();
        Assert.Equal(result.Value.Created.Id, saved.Id);
    }

    [Fact]
    public async Task WriteAsync_Closes_Existing_Open_Record()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);

        var existing = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2025, 1, 1), SalaryType.Annual, 40000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        context.Compensations.Add(existing);
        await context.SaveChangesAsync();

        var writer = new CompensationRecordWriter(context, new FakeClock(FixedUtcNow));

        var result = await writer.WriteAsync(
            companyId, employee.Id, new DateOnly(2026, 1, 1), SalaryType.Annual, 45000m,
            "GBP", null, null, null, CompensationChangeReason.AnnualReview, ActorId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.ClosedPrevious);
        Assert.Equal(existing.Id, result.Value.ClosedPrevious!.Id);
        Assert.Equal(new DateOnly(2025, 12, 31), result.Value.ClosedPrevious.EffectiveTo);
    }

    [Fact]
    public async Task WriteAsync_Returns_Conflict_When_EffectiveFrom_Overlaps_Open_Record_Starting_Later_Or_Same_Day()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);

        var existing = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2026, 1, 1), SalaryType.Annual, 40000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        context.Compensations.Add(existing);
        await context.SaveChangesAsync();

        var writer = new CompensationRecordWriter(context, new FakeClock(FixedUtcNow));

        var result = await writer.WriteAsync(
            companyId, employee.Id, new DateOnly(2026, 1, 1), SalaryType.Annual, 45000m,
            "GBP", null, null, null, CompensationChangeReason.AnnualReview, ActorId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);

        var unchanged = await context.Compensations.SingleAsync(c => c.Id == existing.Id);
        Assert.Null(unchanged.EffectiveTo);
    }

    [Fact]
    public async Task WriteAsync_Returns_Conflict_When_Backdated_Into_Closed_Historical_Record()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);

        var historical = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2024, 1, 1), SalaryType.Annual, 35000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        historical.Close(new DateOnly(2024, 12, 31), now);
        context.Compensations.Add(historical);
        await context.SaveChangesAsync();

        var writer = new CompensationRecordWriter(context, new FakeClock(FixedUtcNow));

        var result = await writer.WriteAsync(
            companyId, employee.Id, new DateOnly(2024, 6, 1), SalaryType.Annual, 36000m,
            "GBP", null, null, null, CompensationChangeReason.Correction, ActorId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    private static Employee CreateEmployee(Guid companyId, DateTimeOffset now) =>
        Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new EmployeesDbContext(options);
    }
}
