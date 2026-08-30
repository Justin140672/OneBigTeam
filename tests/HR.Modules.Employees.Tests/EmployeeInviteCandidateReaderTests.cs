using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class EmployeeInviteCandidateReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static PositionProfile SeedPosition(EmployeesDbContext db, Guid companyId, string title)
    {
        var profile = PositionProfile.Create(
            Guid.NewGuid(), companyId, departmentId: Guid.NewGuid(), locationId: Guid.NewGuid(), title,
            description: null, probationMonthsOverride: null, workingDaysOverride: null,
            hoursPerDayOverride: null, salaryMin: null, salaryMax: null, salaryType: null,
            defaultLeavePolicyId: Guid.NewGuid(), Now);
        db.PositionProfiles.Add(profile);
        return profile;
    }

    private static Employee SeedEmployee(
        EmployeesDbContext db,
        Guid companyId,
        string firstName,
        string lastName,
        string workEmail,
        Guid positionProfileId,
        EmploymentStatus? status = null)
    {
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, firstName, lastName, workEmail, StartDate, hasSystemAccess: false,
            new DateOnly(1990, 1, 1), "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), positionProfileId, Now);

        if (status is { } s)
            employee.SetStatusForTesting(s, Now);

        db.Employees.Add(employee);
        return employee;
    }

    [Fact]
    public async Task GetCandidatesAsync_Returns_NonFormer_Employees_With_Name_Email_And_Position()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var position = SeedPosition(db, companyId, "Software Engineer");
        var employee = SeedEmployee(db, companyId, "Alice", "Adams", "alice@test.com", position.Id);
        await db.SaveChangesAsync();

        var reader = new EmployeeInviteCandidateReader(db);
        var candidates = await reader.GetCandidatesAsync(companyId, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal(employee.Id, candidate.EmployeeId);
        Assert.Equal("Alice Adams", candidate.FullName);
        Assert.Equal("alice@test.com", candidate.WorkEmail);
        Assert.Equal(position.Id, candidate.PositionProfileId);
        Assert.Equal("Software Engineer", candidate.PositionTitle);
    }

    [Fact]
    public async Task GetCandidatesAsync_Excludes_FormerEmployees()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var position = SeedPosition(db, companyId, "Analyst");
        SeedEmployee(db, companyId, "Former", "Person", "former@test.com", position.Id, EmploymentStatus.FormerEmployee);
        var current = SeedEmployee(db, companyId, "Current", "Person", "current@test.com", position.Id);
        await db.SaveChangesAsync();

        var reader = new EmployeeInviteCandidateReader(db);
        var candidates = await reader.GetCandidatesAsync(companyId, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal(current.Id, candidate.EmployeeId);
    }

    [Fact]
    public async Task GetCandidatesAsync_Returns_Null_WorkEmail_When_Blank()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var position = SeedPosition(db, companyId, "Analyst");
        SeedEmployee(db, companyId, "No", "Email", "   ", position.Id);
        await db.SaveChangesAsync();

        var reader = new EmployeeInviteCandidateReader(db);
        var candidates = await reader.GetCandidatesAsync(companyId, CancellationToken.None);

        Assert.Null(Assert.Single(candidates).WorkEmail);
    }

    [Fact]
    public async Task GetCandidatesAsync_Returns_Null_PositionTitle_When_Profile_Missing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        SeedEmployee(db, companyId, "Orphan", "Position", "orphan@test.com", positionProfileId: Guid.NewGuid());
        await db.SaveChangesAsync();

        var reader = new EmployeeInviteCandidateReader(db);
        var candidates = await reader.GetCandidatesAsync(companyId, CancellationToken.None);

        Assert.Null(Assert.Single(candidates).PositionTitle);
    }

    [Fact]
    public async Task GetCandidatesAsync_Orders_By_Name_Case_Insensitively()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var position = SeedPosition(db, companyId, "Analyst");
        SeedEmployee(db, companyId, "zoe", "young", "zoe@test.com", position.Id);
        SeedEmployee(db, companyId, "Anna", "Archer", "anna@test.com", position.Id);
        SeedEmployee(db, companyId, "Mike", "Miller", "mike@test.com", position.Id);
        await db.SaveChangesAsync();

        var reader = new EmployeeInviteCandidateReader(db);
        var candidates = await reader.GetCandidatesAsync(companyId, CancellationToken.None);

        Assert.Equal(["Anna Archer", "Mike Miller", "zoe young"], candidates.Select(c => c.FullName));
    }

    [Fact]
    public async Task GetCandidatesAsync_Is_Scoped_To_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var position = SeedPosition(db, otherCompanyId, "Analyst");
        SeedEmployee(db, otherCompanyId, "Other", "Company", "other@test.com", position.Id);
        await db.SaveChangesAsync();

        var reader = new EmployeeInviteCandidateReader(db);
        var candidates = await reader.GetCandidatesAsync(companyId, CancellationToken.None);

        Assert.Empty(candidates);
    }
}
