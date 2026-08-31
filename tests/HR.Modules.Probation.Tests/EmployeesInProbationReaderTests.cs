using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

/// <summary>
/// DSH-05: <see cref="EmployeesInProbationReader"/> — "in probation" means an active probation
/// record (Active / ReviewDue / Extended). NotStarted, Passed, Failed and NotApplicable are not.
/// </summary>
public class EmployeesInProbationReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 30);

    private static ProbationDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static void SeedRecord(ProbationDbContext db, Guid companyId, Guid employeeId, string status)
    {
        ProbationRecord record;
        switch (status)
        {
            case "NotStarted":
                record = ProbationRecord.Create(
                    Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
                    Today.AddDays(10), Today.AddDays(100), null, Today, Now);
                break;
            case "NotApplicable":
                record = ProbationRecord.CreateNotApplicable(
                    Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
                    Today.AddDays(-30), Today.AddDays(60), "exempt role", Now);
                break;
            default:
                record = ProbationRecord.Create(
                    Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
                    Today.AddDays(-30), Today.AddDays(60), null, Today, Now);
                switch (status)
                {
                    case "Active":
                        break;
                    case "ReviewDue":
                        record.MarkReviewDue(Now);
                        break;
                    case "Extended":
                        record.Extend(Today.AddDays(90), "needs more time", Guid.NewGuid(), Today, Now);
                        break;
                    case "Passed":
                        record.Pass(Guid.NewGuid(), Today, null, Now);
                        break;
                    case "Failed":
                        record.Fail(Guid.NewGuid(), Today, null, Now);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(status), status, null);
                }

                break;
        }

        db.ProbationRecords.Add(record);
    }

    private static Task<IReadOnlySet<Guid>> Read(ProbationDbContext db, Guid companyId, params Guid[] ids) =>
        new EmployeesInProbationReader(db).GetEmployeeIdsInProbationAsync(companyId, ids, CancellationToken.None);

    [Theory]
    [InlineData("Active", true)]
    [InlineData("ReviewDue", true)]
    [InlineData("Extended", true)]
    [InlineData("Passed", false)]
    [InlineData("Failed", false)]
    [InlineData("NotStarted", false)]
    [InlineData("NotApplicable", false)]
    public async Task Includes_Only_Active_Probation_Record_Statuses(string status, bool expectedIncluded)
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        SeedRecord(db, companyId, employeeId, status);
        await db.SaveChangesAsync();

        var result = await Read(db, companyId, employeeId);

        Assert.Equal(expectedIncluded, result.Contains(employeeId));
    }

    [Fact]
    public async Task Returns_Only_Requested_Ids()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var inScope = Guid.NewGuid();
        var notRequested = Guid.NewGuid();
        SeedRecord(db, companyId, inScope, "Active");
        SeedRecord(db, companyId, notRequested, "Active");
        await db.SaveChangesAsync();

        var result = await Read(db, companyId, inScope);

        Assert.Equal(new[] { inScope }, result);
    }

    [Fact]
    public async Task Is_Scoped_By_Company()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        SeedRecord(db, companyB, employeeId, "Active");
        await db.SaveChangesAsync();

        var result = await Read(db, companyA, employeeId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Empty_Id_List_Short_Circuits_To_Empty()
    {
        await using var db = BuildContext();
        var result = await Read(db, Guid.NewGuid());
        Assert.Empty(result);
    }

    [Fact]
    public async Task Deduplicates_Multiple_Active_Records_For_The_Same_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        SeedRecord(db, companyId, employeeId, "Active");
        SeedRecord(db, companyId, employeeId, "ReviewDue");
        await db.SaveChangesAsync();

        var result = await Read(db, companyId, employeeId);

        Assert.Single(result);
    }
}
