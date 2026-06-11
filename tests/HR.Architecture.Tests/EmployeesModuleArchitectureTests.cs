using System.Reflection;
using HR.Modules.Employees;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class EmployeesModuleArchitectureTests
{
    private static readonly Assembly ModuleAssembly = typeof(EmployeesModule).Assembly;

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        return new EmployeesDbContext(options);
    }

    [Fact]
    public void Employees_Module_Only_Exposes_Registration_Surface_As_Public()
    {
        var unexpected = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name is not "EmployeesModule")
            .Where(t => t.Namespace?.Contains(".Migrations") is not true)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected public types in Employees module: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Employees_DbContext_Is_Not_Public()
    {
        var dbContextType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "EmployeesDbContext");

        Assert.False(dbContextType.IsPublic, "EmployeesDbContext must be internal, not public.");
    }

    [Fact]
    public void Employee_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "Employee");

        Assert.False(entityType.IsPublic, "Employee entity must be internal, not public.");
    }

    [Fact]
    public void Employees_DbContext_Uses_Employees_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("employees", context.Model.GetDefaultSchema());
    }

    [Fact]
    public void Employee_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(Employee))!;

        Assert.Equal("employees", entityType.GetTableName());
        Assert.Equal("employees", entityType.GetSchema());
    }

    [Fact]
    public void Employee_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(Employee))!;
        var pk = entityType.FindPrimaryKey()!;

        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void Employee_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(Employee))!;

        var violations = entityType
            .GetProperties()
            .Select(p => p.GetColumnName())
            .Where(name => name.Any(char.IsUpper))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Column names must be snake_case. Violations: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Department_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(Department))!;

        Assert.Equal("departments", entityType.GetTableName());
        Assert.Equal("employees", entityType.GetSchema());
    }

    [Fact]
    public void Department_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(Department))!;
        var pk = entityType.FindPrimaryKey()!;

        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }
}
