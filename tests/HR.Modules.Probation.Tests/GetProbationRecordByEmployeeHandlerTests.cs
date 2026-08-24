using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.GetProbationRecordByEmployee;
using HR.Modules.Probation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class GetProbationRecordByEmployeeHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Record_When_Found()
    {
        await using var context = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId  = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, managerId,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), "Some notes.", DateOnly.FromDateTime(Now.UtcDateTime), Now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new GetProbationRecordByEmployeeHandler(context);

        var result = await handler.HandleAsync(
            new GetProbationRecordByEmployeeRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(record.Id, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal(employeeId, result.Value.EmployeeId);
        Assert.Equal(managerId, result.Value.ManagerEmployeeId);
        Assert.Equal("Active", result.Value.Status);
        Assert.Equal("Some notes.", result.Value.Notes);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Has_No_Record()
    {
        await using var context = BuildContext();
        var handler = new GetProbationRecordByEmployeeHandler(context);

        var result = await handler.HandleAsync(
            new GetProbationRecordByEmployeeRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_CompanyId_Does_Not_Match()
    {
        await using var context = BuildContext();
        var employeeId = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), employeeId, Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new GetProbationRecordByEmployeeHandler(context);

        var result = await handler.HandleAsync(
            new GetProbationRecordByEmployeeRequest { CompanyId = Guid.NewGuid(), EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Most_Recent_Record_When_Employee_Has_Multiple()
    {
        await using var context = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var older = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 1), "older", DateOnly.FromDateTime(Now.AddMonths(-6).UtcDateTime), Now.AddMonths(-6));
        var newer = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), "newer", DateOnly.FromDateTime(Now.UtcDateTime), Now);

        context.ProbationRecords.AddRange(older, newer);
        await context.SaveChangesAsync();

        var handler = new GetProbationRecordByEmployeeHandler(context);

        var result = await handler.HandleAsync(
            new GetProbationRecordByEmployeeRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newer.Id, result.Value!.Id);
    }

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
