using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

/// <summary>
/// Ticket 8 — "Equality Data Retention and Employee Deletion".
///
/// Equality-monitoring data must follow the employee lifecycle and must never be able to survive
/// as an identifiable orphan. These tests pin the EF model: a real foreign key from
/// <see cref="EmployeeEqualityData"/> to <see cref="Employee"/> on <c>EmployeeId</c>, configured
/// with <see cref="DeleteBehavior.Cascade"/> so a physical delete of the employee row destroys the
/// special-category record.
/// </summary>
public class EmployeeEqualityDataModelTests
{
    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new EmployeesDbContext(options);
    }

    [Fact]
    public void EmployeeEqualityData_Has_ForeignKey_On_EmployeeId_Targeting_Employee()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(EmployeeEqualityData))!;

        var fk = entityType.GetForeignKeys().SingleOrDefault(f =>
            f.Properties.Count == 1
            && f.Properties[0].Name == nameof(EmployeeEqualityData.EmployeeId));

        Assert.NotNull(fk);
        Assert.Equal(typeof(Employee), fk!.PrincipalEntityType.ClrType);
        Assert.Equal(nameof(Employee.Id), Assert.Single(fk.PrincipalKey.Properties).Name);
    }

    [Fact]
    public void EmployeeEqualityData_To_Employee_ForeignKey_Uses_Cascade_Delete()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(EmployeeEqualityData))!;

        var fk = entityType.GetForeignKeys().Single(f =>
            f.Properties.Count == 1
            && f.Properties[0].Name == nameof(EmployeeEqualityData.EmployeeId)
            && f.PrincipalEntityType.ClrType == typeof(Employee));

        Assert.Equal(DeleteBehavior.Cascade, fk.DeleteBehavior);
    }

    [Fact]
    public void EmployeeEqualityData_EmployeeId_ForeignKey_Is_Required()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(EmployeeEqualityData))!;

        var fk = entityType.GetForeignKeys().Single(f =>
            f.Properties.Count == 1
            && f.Properties[0].Name == nameof(EmployeeEqualityData.EmployeeId)
            && f.PrincipalEntityType.ClrType == typeof(Employee));

        Assert.True(fk.IsRequired, "A NULL employee_id would allow an orphan equality-monitoring row.");
    }
}
