using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

public class ProbationSummaryReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task GetSummaryAsync_Returns_Null_When_No_Record_Exists()
    {
        await using var db = BuildContext();

        var reader = new ProbationSummaryReader(db);
        var result = await reader.GetSummaryAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSummaryAsync_Returns_Most_Recent_Record_When_Employee_Has_Multiple()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var older = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2025, 1, 1), new DateOnly(2025, 4, 1), "older", Now.AddMonths(-6));
        var newer = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), "newer", Now);

        db.ProbationRecords.AddRange(older, newer);
        await db.SaveChangesAsync();

        var reader = new ProbationSummaryReader(db);
        var result = await reader.GetSummaryAsync(companyId, employeeId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(newer.StartDate, result!.StartDate);
    }

    [Fact]
    public async Task GetSummaryAsync_Is_Scoped_By_CompanyId()
    {
        await using var db = BuildContext();
        var employeeId = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), employeeId, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), null, Now);
        db.ProbationRecords.Add(record);
        await db.SaveChangesAsync();

        var reader = new ProbationSummaryReader(db);
        var result = await reader.GetSummaryAsync(Guid.NewGuid(), employeeId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSummaryAsync_Maps_Status_StartDate_ExpectedEndDate_And_DecisionDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 6, 1);
        var expectedEndDate = new DateOnly(2026, 9, 1);
        var decisionDate = new DateOnly(2026, 8, 15);

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            startDate, expectedEndDate, "notes", Now);
        record.Pass(Guid.NewGuid(), decisionDate, "Passed probation", Now);
        db.ProbationRecords.Add(record);
        await db.SaveChangesAsync();

        var reader = new ProbationSummaryReader(db);
        var result = await reader.GetSummaryAsync(companyId, employeeId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Passed", result!.Status);
        Assert.Equal(startDate, result.StartDate);
        Assert.Equal(expectedEndDate, result.ExpectedEndDate);
        Assert.Equal(decisionDate, result.DecisionDate);
    }

    [Fact]
    public async Task GetSummaryAsync_Maps_DecisionDate_As_Null_When_Not_Set()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), null, Now);
        db.ProbationRecords.Add(record);
        await db.SaveChangesAsync();

        var reader = new ProbationSummaryReader(db);
        var result = await reader.GetSummaryAsync(companyId, employeeId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Active", result!.Status);
        Assert.Null(result.DecisionDate);
    }
}
