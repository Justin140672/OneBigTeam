using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class EmployeeWorkEmailUniqueIndexPostgresTests
{
    private readonly ApiWebApplicationFactory _factory;

    public EmployeeWorkEmailUniqueIndexPostgresTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static Employee NewEmployee(
        Guid companyId, EmployeeReferenceDataSeeder.ReferenceData refData, string workEmail) =>
        Employee.Create(
            Guid.NewGuid(), companyId, "Jane", "Smith", workEmail, new DateOnly(2026, 1, 1),
            hasSystemAccess: false, new DateOnly(1990, 1, 1), "British", "Prefer not to say",
            $"EMP-{Guid.NewGuid():N}", refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId,
            refData.PositionProfileId, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Unique_Index_Rejects_A_Duplicate_WorkEmail_In_The_Same_Company()
    {
        var companyId = Guid.NewGuid();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var email = $"jane.smith.{Guid.NewGuid():N}@example.com";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            db.Employees.Add(NewEmployee(companyId, refData, email));
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            db.Employees.Add(NewEmployee(companyId, refData, email));

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
            Assert.Equal("IX_employees_company_id_work_email", postgres.ConstraintName);
        }
    }

    [Fact]
    public async Task Unique_Index_Allows_The_Same_WorkEmail_In_Different_Companies()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var refDataA = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyA);
        var refDataB = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyB);
        var email = $"jane.smith.{Guid.NewGuid():N}@example.com";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        db.Employees.AddRange(NewEmployee(companyA, refDataA, email), NewEmployee(companyB, refDataB, email));

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Employees.CountAsync(e => e.WorkEmail == email));
    }
}
