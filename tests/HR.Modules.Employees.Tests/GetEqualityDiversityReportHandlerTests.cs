using System.Text.Json;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetEqualityDiversityReport;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Modules.Employees.Tests;

public class GetEqualityDiversityReportHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Age26Dob = new(2000, 6, 1);   // 26 on 2026-09-04 -> "25-34"

    private static EmployeesDbContext BuildContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString("N"))
            .Options;
        return new EmployeesDbContext(options, new FakeSensitiveDataProtector());
    }

    private static GetEqualityDiversityReportHandler Handler(EmployeesDbContext db, int minimumGroupSize = 5)
        => new(
            db,
            new FakeClock(Now),
            Options.Create(new EqualityDiversityReportOptions { MinimumGroupSize = minimumGroupSize }));

    private static Guid AddEmployee(EmployeesDbContext db, Guid companyId, DateOnly dob)
    {
        var id = Guid.NewGuid();
        db.Employees.Add(Employee.Create(
            id, companyId, "Test", "Person", $"{id:N}@example.com",
            new DateOnly(2024, 1, 1), hasSystemAccess: false, dob, "British", "Prefer not to say",
            $"EMP-{id:N}", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateTimeOffset(Now)));
        return id;
    }

    private static void AddRecord(
        EmployeesDbContext db, Guid companyId, Guid employeeId,
        EthnicGroup? ethnicGroup = null, GenderIdentity? gender = null, CaringResponsibilities? caring = null)
    {
        db.EmployeeEqualityData.Add(EmployeeEqualityData.Create(
            Guid.NewGuid(), companyId, employeeId,
            gender?.ToString(), null, null,
            ethnicGroup?.ToString(), null,
            null, null,
            null, null,
            null, null,
            caring?.ToString(),
            new DateTimeOffset(Now)));
    }

    /// <summary>
    /// Company of 15: 10 aged 25-34, 5 with an unknown DOB. 13 equality records
    /// (7 White, 3 Mixed, 3 Asian; 8 Woman, 5 Man), 2 employees with no record at all.
    /// </summary>
    private static async Task<(EmployeesDbContext Db, Guid CompanyId, List<Guid> EmployeeIds)> SeededCompanyAsync()
    {
        var db = BuildContext();
        var companyId = Guid.NewGuid();
        var ids = new List<Guid>();

        for (var i = 0; i < 10; i++) ids.Add(AddEmployee(db, companyId, Age26Dob));
        for (var i = 0; i < 5; i++) ids.Add(AddEmployee(db, companyId, default));

        // Another company's employee — must never bleed into the totals.
        AddEmployee(db, Guid.NewGuid(), Age26Dob);

        var ethnic = new[]
        {
            EthnicGroup.White, EthnicGroup.White, EthnicGroup.White, EthnicGroup.White,
            EthnicGroup.White, EthnicGroup.White, EthnicGroup.White,
            EthnicGroup.Mixed, EthnicGroup.Mixed, EthnicGroup.Mixed,
            EthnicGroup.AsianOrAsianBritish, EthnicGroup.AsianOrAsianBritish, EthnicGroup.AsianOrAsianBritish,
        };
        for (var i = 0; i < ethnic.Length; i++)
        {
            var gender = i < 8 ? GenderIdentity.Woman : GenderIdentity.Man;
            AddRecord(db, companyId, ids[i], ethnic[i], gender);
        }
        // ids[13], ids[14] deliberately have no record.

        await db.SaveChangesAsync();
        return (db, companyId, ids);
    }

    private static EqualityReportDimension Dim(GetEqualityDiversityReportResponse r, string key)
        => Assert.Single(r.Dimensions, d => d.Key == key);

    [Fact]
    public async Task Reports_Expected_Dimensions_Total_And_Threshold()
    {
        var (db, companyId, _) = await SeededCompanyAsync();
        await using var _db = db;

        var result = await Handler(db).HandleAsync(new GetEqualityDiversityReportRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var report = result.Value!;
        Assert.Equal(15, report.TotalEmployees);
        Assert.Equal(5, report.MinimumGroupSize);
        Assert.Equal(
            new[] { "gender", "age-band", "ethnicity", "disability", "sexual-orientation", "religion-or-belief", "caring-responsibilities" }.OrderBy(x => x),
            report.Dimensions.Select(d => d.Key).OrderBy(x => x));
    }

    [Fact]
    public async Task Counts_And_Percentages_Are_Per_Dimension_Over_The_Whole_Workforce()
    {
        var (db, companyId, _) = await SeededCompanyAsync();
        await using var _db = db;

        var report = (await Handler(db).HandleAsync(new GetEqualityDiversityReportRequest(companyId), CancellationToken.None)).Value!;

        var gender = Dim(report, "gender");
        var woman = Assert.Single(gender.Rows, x => x.Value == "Woman");
        Assert.Equal(8, woman.Count);
        Assert.False(woman.Suppressed);
        Assert.Equal(Math.Round(8 * 100m / 15, 1), woman.Percentage);
        Assert.Equal(5, Assert.Single(gender.Rows, x => x.Value == "Man").Count);
        Assert.Equal(2, Assert.Single(gender.Rows, x => x.Value == "Not stated").Count);
    }

    [Fact]
    public async Task Employees_With_No_Record_Are_Counted_As_Not_Stated()
    {
        var (db, companyId, _) = await SeededCompanyAsync();
        await using var _db = db;

        var report = (await Handler(db).HandleAsync(new GetEqualityDiversityReportRequest(companyId), CancellationToken.None)).Value!;

        // Nobody supplied a disability answer -> every one of the 15 is "Not stated".
        var disability = Dim(report, "disability");
        var notStated = Assert.Single(disability.Rows);
        Assert.Equal("Not stated", notStated.Value);
        Assert.Equal(15, notStated.Count);
        Assert.False(notStated.Suppressed);
    }

    [Fact]
    public async Task Age_Bands_Include_Unknown_For_Missing_Dob()
    {
        var (db, companyId, _) = await SeededCompanyAsync();
        await using var _db = db;

        var report = (await Handler(db).HandleAsync(new GetEqualityDiversityReportRequest(companyId), CancellationToken.None)).Value!;

        var ageBand = Dim(report, "age-band");
        Assert.Equal(10, Assert.Single(ageBand.Rows, x => x.Value == "25-34").Count);
        Assert.Equal(5, Assert.Single(ageBand.Rows, x => x.Value == "Unknown").Count);
    }

    [Fact]
    public async Task Future_Dob_Is_Also_Unknown()
    {
        var db = BuildContext();
        await using var _db = db;
        var companyId = Guid.NewGuid();
        for (var i = 0; i < 6; i++) AddEmployee(db, companyId, new DateOnly(2100, 1, 1));
        await db.SaveChangesAsync();

        var report = (await Handler(db).HandleAsync(new GetEqualityDiversityReportRequest(companyId), CancellationToken.None)).Value!;

        var unknown = Assert.Single(Dim(report, "age-band").Rows);
        Assert.Equal("Unknown", unknown.Value);
        Assert.Equal(6, unknown.Count);
    }

    [Fact]
    public async Task Small_Groups_Are_Collapsed_Into_A_Suppressed_Not_Reported_Row()
    {
        var (db, companyId, _) = await SeededCompanyAsync();
        await using var _db = db;

        var report = (await Handler(db).HandleAsync(new GetEqualityDiversityReportRequest(companyId), CancellationToken.None)).Value!;

        var ethnicity = Dim(report, "ethnicity");

        // Mixed (3) and Asian (3) are each below the threshold of 5.
        Assert.DoesNotContain(ethnicity.Rows, x => x.Value == "Mixed");
        Assert.DoesNotContain(ethnicity.Rows, x => x.Value == "Asian Or Asian British");
        // "Not stated" is itself an aggregate bucket and is never suppressed, so exclude it here.
        Assert.DoesNotContain(ethnicity.Rows, x => x.Value != "Not stated" && x.Count is >= 1 and < 5 && !x.Suppressed);

        var notReported = Assert.Single(ethnicity.Rows, x => x.Value == "Not reported");
        Assert.True(notReported.Suppressed);
        Assert.Equal(6, notReported.Count); // 3 + 3 folded together

        // The real group that clears the threshold is still shown.
        Assert.Equal(7, Assert.Single(ethnicity.Rows, x => x.Value == "White").Count);
        Assert.Equal(2, Assert.Single(ethnicity.Rows, x => x.Value == "Not stated").Count);
    }

    [Fact]
    public async Task Threshold_Below_Two_Falls_Back_To_Five()
    {
        var (db, companyId, _) = await SeededCompanyAsync();
        await using var _db = db;

        var report = (await Handler(db, minimumGroupSize: 1).HandleAsync(
            new GetEqualityDiversityReportRequest(companyId), CancellationToken.None)).Value!;

        Assert.Equal(5, report.MinimumGroupSize);
        // Suppression still applied at 5, not 1.
        Assert.DoesNotContain(Dim(report, "ethnicity").Rows, x => x.Value == "Mixed");
    }

    [Fact]
    public async Task Response_Exposes_No_Employee_Identifiers()
    {
        var (db, companyId, employeeIds) = await SeededCompanyAsync();
        await using var _db = db;

        var report = (await Handler(db).HandleAsync(new GetEqualityDiversityReportRequest(companyId), CancellationToken.None)).Value!;

        var json = JsonSerializer.Serialize(report);
        foreach (var id in employeeIds)
            Assert.DoesNotContain(id.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(companyId.ToString(), json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Caring_Responsibilities_Dimension_Aggregates_Answers()
    {
        var db = BuildContext();
        await using var _db = db;
        var companyId = Guid.NewGuid();
        var ids = new List<Guid>();
        for (var i = 0; i < 12; i++) ids.Add(AddEmployee(db, companyId, Age26Dob));
        for (var i = 0; i < 12; i++)
            AddRecord(db, companyId, ids[i], caring: i < 7 ? CaringResponsibilities.Yes : CaringResponsibilities.No);
        await db.SaveChangesAsync();

        var report = (await Handler(db).HandleAsync(new GetEqualityDiversityReportRequest(companyId), CancellationToken.None)).Value!;

        var caring = Dim(report, "caring-responsibilities");
        Assert.Equal(7, Assert.Single(caring.Rows, x => x.Value == "Yes").Count);
        Assert.Equal(5, Assert.Single(caring.Rows, x => x.Value == "No").Count);
    }
}
