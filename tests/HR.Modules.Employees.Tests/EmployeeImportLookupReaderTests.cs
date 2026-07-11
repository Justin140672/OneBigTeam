using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class EmployeeImportLookupReaderTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }

    private static Employee SeedEmployee(
        EmployeesDbContext db,
        Guid companyId,
        string workEmail,
        string? employeeNumber = null)
    {
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", workEmail, StartDate, hasSystemAccess: false, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        if (employeeNumber is not null)
        {
            employee.UpdateEmploymentDetails(employeeNumber, Guid.NewGuid(), StartDate, null, null, null, null, Now);
        }

        db.Employees.Add(employee);
        db.SaveChanges();
        return employee;
    }

    [Fact]
    public async Task EmployeeNumberExistsAsync_Returns_True_When_Employee_Number_Exists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        SeedEmployee(db, companyId, "alice@example.com", "EMP001");

        var reader = new EmployeeImportLookupReader(db);

        var exists = await reader.EmployeeNumberExistsAsync(companyId, "EMP001", CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public async Task EmployeeNumberExistsAsync_Returns_False_When_Employee_Number_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        SeedEmployee(db, companyId, "alice@example.com", "EMP001");

        var reader = new EmployeeImportLookupReader(db);

        var exists = await reader.EmployeeNumberExistsAsync(companyId, "EMP999", CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task EmployeeNumberExistsAsync_Matches_Case_Insensitively_And_Trimmed()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        SeedEmployee(db, companyId, "alice@example.com", "EMP001");

        var reader = new EmployeeImportLookupReader(db);

        var exists = await reader.EmployeeNumberExistsAsync(companyId, "  emp001  ", CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public async Task EmployeeNumberExistsAsync_Scoped_To_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        SeedEmployee(db, otherCompanyId, "alice@example.com", "EMP001");

        var reader = new EmployeeImportLookupReader(db);

        var exists = await reader.EmployeeNumberExistsAsync(companyId, "EMP001", CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task WorkEmailExistsAsync_Returns_True_When_Work_Email_Exists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        // Production normalizes work emails to lowercase before saving (see CreateEmployeeHandler);
        // seed data the same way here.
        SeedEmployee(db, companyId, "alice@example.com");

        var reader = new EmployeeImportLookupReader(db);

        var exists = await reader.WorkEmailExistsAsync(companyId, "alice@example.com", CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public async Task WorkEmailExistsAsync_Returns_False_When_Work_Email_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        SeedEmployee(db, companyId, "alice@example.com");

        var reader = new EmployeeImportLookupReader(db);

        var exists = await reader.WorkEmailExistsAsync(companyId, "nobody@example.com", CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task WorkEmailExistsAsync_Matches_Case_Insensitively_And_Trimmed()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        SeedEmployee(db, companyId, "alice@example.com");

        var reader = new EmployeeImportLookupReader(db);

        var exists = await reader.WorkEmailExistsAsync(companyId, "  Alice@EXAMPLE.com  ", CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public async Task WorkEmailExistsAsync_Scoped_To_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        SeedEmployee(db, otherCompanyId, "alice@example.com");

        var reader = new EmployeeImportLookupReader(db);

        var exists = await reader.WorkEmailExistsAsync(companyId, "alice@example.com", CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task FindEmployeeIdByReferenceAsync_Resolves_By_EmployeeNumber()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = SeedEmployee(db, companyId, "manager@example.com", "MGR001");

        var reader = new EmployeeImportLookupReader(db);

        var resolved = await reader.FindEmployeeIdByReferenceAsync(companyId, "  mgr001  ", CancellationToken.None);

        Assert.Equal(employee.Id, resolved);
    }

    [Fact]
    public async Task FindEmployeeIdByReferenceAsync_Resolves_By_WorkEmail()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = SeedEmployee(db, companyId, "manager@example.com");

        var reader = new EmployeeImportLookupReader(db);

        var resolved = await reader.FindEmployeeIdByReferenceAsync(companyId, "  Manager@Example.com  ", CancellationToken.None);

        Assert.Equal(employee.Id, resolved);
    }

    [Fact]
    public async Task FindEmployeeIdByReferenceAsync_Returns_Null_When_No_Match()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        SeedEmployee(db, companyId, "manager@example.com", "MGR001");

        var reader = new EmployeeImportLookupReader(db);

        var resolved = await reader.FindEmployeeIdByReferenceAsync(companyId, "nobody@example.com", CancellationToken.None);

        Assert.Null(resolved);
    }
}
