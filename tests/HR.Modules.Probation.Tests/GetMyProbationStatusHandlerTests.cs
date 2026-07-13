using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.GetMyProbationStatus;
using HR.Modules.Probation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class GetMyProbationStatusHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_HasRecord_False_When_No_Record_Exists()
    {
        await using var context = BuildContext();
        var handler = new GetMyProbationStatusHandler(context);

        var result = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.HasRecord);
        Assert.Null(result.Id);
        Assert.Null(result.StartDate);
        Assert.Null(result.ExpectedEndDate);
        Assert.Null(result.Status);
        Assert.Null(result.DecisionDate);
        Assert.Null(result.OutcomeNotes);
    }

    [Fact]
    public async Task HandleAsync_Returns_Record_When_Found()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), "Some notes.", Now);
        context.ProbationRecords.Add(record);
        await context.SaveChangesAsync();

        var handler = new GetMyProbationStatusHandler(context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.HasRecord);
        Assert.Equal(record.Id, result.Id);
        Assert.Equal(new DateOnly(2026, 6, 1), result.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 1), result.ExpectedEndDate);
        Assert.Equal("Active", result.Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_Most_Recent_Record_When_Employee_Has_Multiple()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var older = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 1), "older", Now.AddMonths(-6));
        var newer = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), "newer", Now);

        context.ProbationRecords.AddRange(older, newer);
        await context.SaveChangesAsync();

        var handler = new GetMyProbationStatusHandler(context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.True(result.HasRecord);
        Assert.Equal(newer.Id, result.Id);
    }

    [Fact]
    public async Task HandleAsync_Isolates_By_Company()
    {
        await using var context = BuildContext();
        var employeeId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        var otherCompanyRecord = ProbationRecord.Create(
            Guid.NewGuid(), otherCompanyId, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, Now);
        context.ProbationRecords.Add(otherCompanyRecord);
        await context.SaveChangesAsync();

        var handler = new GetMyProbationStatusHandler(context);

        var result = await handler.HandleAsync(companyId, employeeId, CancellationToken.None);

        Assert.False(result.HasRecord);
    }

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
