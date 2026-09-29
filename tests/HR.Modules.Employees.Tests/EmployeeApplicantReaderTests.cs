using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

/// <summary>
/// Internal recruitment Ticket 4: the company-scoped <see cref="IEmployeeApplicantReader"/> used by
/// Recruitment's internal-vacancy Apply slice to take the applicant's identity and employment state
/// from the authoritative Employee record.
/// </summary>
public class EmployeeApplicantReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2025, 1, 6);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Employee SeedEmployee(
        EmployeesDbContext db,
        Guid companyId,
        string firstName = "Priya",
        string lastName = "Shah",
        string workEmail = "priya.shah@acme.example",
        string? phoneNumber = "07700 900456",
        EmploymentStatus? status = EmploymentStatus.Active)
    {
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, firstName, lastName, workEmail, StartDate, hasSystemAccess: true,
            new DateOnly(1990, 1, 1), "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        if (phoneNumber is not null)
            employee.UpdateContactDetails(null, phoneNumber, null, null, null, null, null, null, null, Now);

        if (status is { } s)
            employee.SetStatusForTesting(s, Now);

        db.Employees.Add(employee);
        return employee;
    }

    [Fact]
    public async Task GetApplicantAsync_Returns_Identity_And_Active_State()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = SeedEmployee(db, companyId);
        await db.SaveChangesAsync();

        var profile = await new EmployeeApplicantReader(db).GetApplicantAsync(companyId, employee.Id, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal(employee.Id, profile.EmployeeId);
        Assert.Equal(companyId, profile.CompanyId);
        Assert.Equal("Priya", profile.FirstName);
        Assert.Equal("Shah", profile.LastName);
        Assert.Equal("priya.shah@acme.example", profile.WorkEmail);
        Assert.Equal("07700 900456", profile.PhoneNumber);
        Assert.Equal(EmployeeApplicantEmploymentState.Active, profile.EmploymentState);
    }

    [Fact]
    public async Task GetApplicantAsync_Returns_Null_Phone_When_Employee_Has_None()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = SeedEmployee(db, companyId, phoneNumber: null);
        await db.SaveChangesAsync();

        var profile = await new EmployeeApplicantReader(db).GetApplicantAsync(companyId, employee.Id, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Null(profile.PhoneNumber);
    }

    [Theory]
    [InlineData(nameof(EmploymentStatus.Draft), nameof(EmployeeApplicantEmploymentState.Draft))]
    [InlineData(nameof(EmploymentStatus.Active), nameof(EmployeeApplicantEmploymentState.Active))]
    [InlineData(nameof(EmploymentStatus.Suspended), nameof(EmployeeApplicantEmploymentState.Suspended))]
    [InlineData(nameof(EmploymentStatus.Leaving), nameof(EmployeeApplicantEmploymentState.Leaving))]
    [InlineData(nameof(EmploymentStatus.FormerEmployee), nameof(EmployeeApplicantEmploymentState.Former))]
    public async Task GetApplicantAsync_Maps_Each_EmploymentStatus(string statusName, string expectedStateName)
    {
        var status = Enum.Parse<EmploymentStatus>(statusName);
        var expected = Enum.Parse<EmployeeApplicantEmploymentState>(expectedStateName);
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = SeedEmployee(db, companyId, status: status);
        await db.SaveChangesAsync();

        var profile = await new EmployeeApplicantReader(db).GetApplicantAsync(companyId, employee.Id, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal(expected, profile.EmploymentState);
    }

    [Fact]
    public async Task GetApplicantAsync_Newly_Created_Employee_Is_Draft()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = SeedEmployee(db, companyId, status: null);
        await db.SaveChangesAsync();

        var profile = await new EmployeeApplicantReader(db).GetApplicantAsync(companyId, employee.Id, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal(EmployeeApplicantEmploymentState.Draft, profile.EmploymentState);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(99)]
    public void MapState_Fails_Closed_To_Draft_For_Retired_Or_Unknown_Values(int rawStatus)
    {
        Assert.Equal(EmployeeApplicantEmploymentState.Draft, EmployeeApplicantReader.MapState((EmploymentStatus)rawStatus));
    }

    [Fact]
    public async Task GetApplicantAsync_Returns_Null_For_Unknown_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        SeedEmployee(db, companyId);
        await db.SaveChangesAsync();

        var profile = await new EmployeeApplicantReader(db).GetApplicantAsync(companyId, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(profile);
    }

    [Fact]
    public async Task GetApplicantAsync_Returns_Null_For_Employee_Of_Another_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var otherCompanyEmployee = SeedEmployee(db, otherCompanyId);
        await db.SaveChangesAsync();

        var profile = await new EmployeeApplicantReader(db).GetApplicantAsync(companyId, otherCompanyEmployee.Id, CancellationToken.None);

        Assert.Null(profile);
    }

    [Fact]
    public async Task GetApplicantAsync_Returns_The_Requested_Employee_Among_Several()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        SeedEmployee(db, companyId, "Tom", "Baker", "tom.baker@acme.example");
        var target = SeedEmployee(db, companyId, "Priya", "Shah", "priya.shah@acme.example");
        SeedEmployee(db, companyId, "Ann", "Lee", "ann.lee@acme.example");
        await db.SaveChangesAsync();

        var profile = await new EmployeeApplicantReader(db).GetApplicantAsync(companyId, target.Id, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal(target.Id, profile.EmployeeId);
        Assert.Equal("priya.shah@acme.example", profile.WorkEmail);
    }
}
