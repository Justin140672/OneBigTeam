using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class PositionProfileReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static PositionProfile Seed(
        EmployeesDbContext db,
        Guid companyId,
        Guid? departmentId,
        string title,
        bool isActive = true)
    {
        var profile = PositionProfile.Create(
            Guid.NewGuid(), companyId, departmentId ?? Guid.NewGuid(), locationId: Guid.NewGuid(), title,
            description: null, probationMonthsOverride: null, workingDaysOverride: null,
            hoursPerDayOverride: null, salaryMin: null, salaryMax: null, salaryType: null,
            defaultLeavePolicyId: Guid.NewGuid(), Now);

        if (!isActive)
            profile.Deactivate(Now);

        db.PositionProfiles.Add(profile);
        return profile;
    }

    [Fact]
    public async Task FindActiveMatchesAsync_Returns_Exact_Case_Insensitive_Title_Match()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var profile = Seed(db, companyId, departmentId, "Senior Software Engineer");
        await db.SaveChangesAsync();

        var reader = new PositionProfileReader(db);
        var result = await reader.FindActiveMatchesAsync(companyId, departmentId, "senior software engineer", CancellationToken.None);

        Assert.Equal([profile.Id], result);
    }

    [Fact]
    public async Task FindActiveMatchesAsync_Applies_Department_Filter_When_DepartmentId_Supplied()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var engineeringId = Guid.NewGuid();
        var salesId = Guid.NewGuid();
        var engineeringProfile = Seed(db, companyId, engineeringId, "Account Manager");
        Seed(db, companyId, salesId, "Account Manager");
        await db.SaveChangesAsync();

        var reader = new PositionProfileReader(db);
        var result = await reader.FindActiveMatchesAsync(companyId, engineeringId, "Account Manager", CancellationToken.None);

        Assert.Equal([engineeringProfile.Id], result);
    }

    [Fact]
    public async Task FindActiveMatchesAsync_Is_Company_Wide_When_DepartmentId_Is_Null()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var engineeringProfile = Seed(db, companyId, Guid.NewGuid(), "Account Manager");
        var salesProfile = Seed(db, companyId, Guid.NewGuid(), "Account Manager");
        await db.SaveChangesAsync();

        var reader = new PositionProfileReader(db);
        var result = await reader.FindActiveMatchesAsync(companyId, null, "Account Manager", CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(engineeringProfile.Id, result);
        Assert.Contains(salesProfile.Id, result);
    }

    [Fact]
    public async Task FindActiveMatchesAsync_Excludes_Inactive_PositionProfiles()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        Seed(db, companyId, null, "Account Manager", isActive: false);
        await db.SaveChangesAsync();

        var reader = new PositionProfileReader(db);
        var result = await reader.FindActiveMatchesAsync(companyId, null, "Account Manager", CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindActiveMatchesAsync_Excludes_PositionProfiles_From_Other_Companies()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        Seed(db, otherCompanyId, null, "Account Manager");
        await db.SaveChangesAsync();

        var reader = new PositionProfileReader(db);
        var result = await reader.FindActiveMatchesAsync(companyId, null, "Account Manager", CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindActiveMatchesAsync_Trims_Whitespace_From_Search_Title()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var profile = Seed(db, companyId, null, "Account Manager");
        await db.SaveChangesAsync();

        var reader = new PositionProfileReader(db);
        var result = await reader.FindActiveMatchesAsync(companyId, null, "  Account Manager  ", CancellationToken.None);

        Assert.Equal([profile.Id], result);
    }

    [Fact]
    public async Task FindActiveMatchesAsync_Returns_Empty_When_No_Title_Matches()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        Seed(db, companyId, null, "Account Manager");
        await db.SaveChangesAsync();

        var reader = new PositionProfileReader(db);
        var result = await reader.FindActiveMatchesAsync(companyId, null, "Software Engineer", CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSummaryAsync_Returns_Summary_For_Active_Profile()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var profile = Seed(db, companyId, departmentId, "Software Engineer");
        await db.SaveChangesAsync();

        var reader = new PositionProfileReader(db);
        var summary = await reader.GetSummaryAsync(companyId, profile.Id, CancellationToken.None);

        Assert.NotNull(summary);
        Assert.Equal(profile.Id, summary!.Id);
        Assert.Equal("Software Engineer", summary.Title);
        Assert.Equal(departmentId, summary.DepartmentId);
        Assert.True(summary.IsActive);
    }

    [Fact]
    public async Task GetSummaryAsync_Returns_IsActive_False_For_Deactivated_Profile()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var profile = Seed(db, companyId, null, "Software Engineer", isActive: false);
        await db.SaveChangesAsync();

        var reader = new PositionProfileReader(db);

        // Deliberately NOT filtered by IsActive — a deactivated profile must still resolve so
        // read-time displays of existing records (e.g. Vacancy details) keep working.
        var summary = await reader.GetSummaryAsync(companyId, profile.Id, CancellationToken.None);

        Assert.NotNull(summary);
        Assert.Equal("Software Engineer", summary!.Title);
        Assert.False(summary.IsActive);
    }

    [Fact]
    public async Task GetSummaryAsync_Returns_Null_When_Profile_Does_Not_Exist()
    {
        await using var db = BuildContext();

        var reader = new PositionProfileReader(db);
        var summary = await reader.GetSummaryAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Null(summary);
    }

    [Fact]
    public async Task GetSummaryAsync_Returns_Null_When_Profile_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var profile = Seed(db, companyId, null, "Software Engineer");
        await db.SaveChangesAsync();

        var reader = new PositionProfileReader(db);
        var summary = await reader.GetSummaryAsync(otherCompanyId, profile.Id, CancellationToken.None);

        Assert.Null(summary);
    }

    [Fact]
    public async Task GetSummariesAsync_Returns_Summaries_For_Multiple_Requested_Ids()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        var active = Seed(db, companyId, null, "Active Role");
        var inactive = Seed(db, companyId, null, "Inactive Role", isActive: false);
        var otherCompanyProfile = Seed(db, otherCompanyId, null, "Other Company Role");
        await db.SaveChangesAsync();

        var reader = new PositionProfileReader(db);
        var requestedIds = new[] { active.Id, inactive.Id, otherCompanyProfile.Id, Guid.NewGuid() };
        var summaries = await reader.GetSummariesAsync(companyId, requestedIds, CancellationToken.None);

        Assert.Equal(2, summaries.Count);

        var activeSummary = summaries.Single(s => s.Id == active.Id);
        Assert.Equal("Active Role", activeSummary.Title);
        Assert.True(activeSummary.IsActive);

        var inactiveSummary = summaries.Single(s => s.Id == inactive.Id);
        Assert.Equal("Inactive Role", inactiveSummary.Title);
        Assert.False(inactiveSummary.IsActive);

        // Omitted: the other company's profile (tenant isolation) and the unknown ID.
        Assert.DoesNotContain(summaries, s => s.Id == otherCompanyProfile.Id);
    }

    [Fact]
    public async Task GetSummariesAsync_Returns_Empty_List_When_Given_Empty_Id_Collection()
    {
        await using var db = BuildContext();

        var reader = new PositionProfileReader(db);
        var summaries = await reader.GetSummariesAsync(Guid.NewGuid(), Array.Empty<Guid>(), CancellationToken.None);

        Assert.Empty(summaries);
    }
}
