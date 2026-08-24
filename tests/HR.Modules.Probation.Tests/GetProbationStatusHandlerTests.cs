using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.GetProbationStatus;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class GetProbationStatusHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task HandleAsync_Returns_HasRecord_False_When_No_Record_Exists()
    {
        await using var context = BuildContext();
        var handler = new GetProbationStatusHandler(new ProbationStatusReader(context));

        var result = await handler.HandleAsync(
            new GetProbationStatusRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.False(result.HasRecord);
        Assert.Null(result.Status);
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("ReviewDue")]
    [InlineData("Extended")]
    [InlineData("Passed")]
    [InlineData("Failed")]
    public async Task HandleAsync_Returns_HasRecord_True_With_Current_Status(string statusName)
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, DateOnly.FromDateTime(Now.UtcDateTime), Now);

        switch (statusName)
        {
            case "ReviewDue": record.MarkReviewDue(Now); break;
            case "Extended": record.Extend(new DateOnly(2026, 10, 1), "Needs more time", Guid.NewGuid(), new DateOnly(2026, 9, 1), Now); break;
            case "Passed": record.Pass(Guid.NewGuid(), new DateOnly(2026, 9, 1), null, Now); break;
            case "Failed": record.Fail(Guid.NewGuid(), new DateOnly(2026, 9, 1), null, Now); break;
        }

        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new GetProbationStatusHandler(new ProbationStatusReader(context));

        var result = await handler.HandleAsync(
            new GetProbationStatusRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.HasRecord);
        Assert.Equal(statusName, result.Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_Most_Recent_Record_By_StartDate_When_Employee_Has_Multiple()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var older = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2024, 1, 1), new DateOnly(2024, 4, 1), null, DateOnly.FromDateTime(Now.AddYears(-2).UtcDateTime), Now.AddYears(-2));
        older.Fail(Guid.NewGuid(), new DateOnly(2024, 4, 1), null, Now.AddYears(-2));

        var newer = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 4, 7), new DateOnly(2026, 7, 7), null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
        newer.MarkReviewDue(Now);

        context.ProbationRecords.AddRange(older, newer);
        await context.SaveChangesAsync();

        var handler = new GetProbationStatusHandler(new ProbationStatusReader(context));

        var result = await handler.HandleAsync(
            new GetProbationStatusRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.HasRecord);
        Assert.Equal("ReviewDue", result.Status);
    }

    [Fact]
    public async Task HandleAsync_Is_Company_Scoped()
    {
        await using var context = BuildContext();
        var employeeId = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), employeeId, Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, DateOnly.FromDateTime(Now.UtcDateTime), Now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new GetProbationStatusHandler(new ProbationStatusReader(context));

        var result = await handler.HandleAsync(
            new GetProbationStatusRequest { CompanyId = Guid.NewGuid(), EmployeeId = employeeId },
            CancellationToken.None);

        Assert.False(result.HasRecord);
    }
}
