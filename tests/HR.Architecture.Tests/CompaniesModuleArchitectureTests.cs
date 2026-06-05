using System.Reflection;
using HR.Modules.Companies;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class CompaniesModuleArchitectureTests
{
    private static readonly Assembly ModuleAssembly = typeof(CompaniesModule).Assembly;

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        return new CompaniesDbContext(options);
    }

    [Fact]
    public void Companies_Module_Only_Exposes_Registration_Surface_As_Public()
    {
        var unexpected = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name is not "Class1" and not "CompaniesModule")
            .Where(t => t.Namespace?.StartsWith("HR.Modules.Companies.Migrations", StringComparison.Ordinal) is not true)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected public types in Companies module: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Companies_DbContext_Is_Not_Public()
    {
        var dbContextType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "CompaniesDbContext");

        Assert.False(dbContextType.IsPublic, "CompaniesDbContext must be internal, not public.");
    }

    [Fact]
    public void Company_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "Company");

        Assert.False(entityType.IsPublic, "Company entity must be internal, not public.");
    }

    [Fact]
    public void Companies_DbContext_Uses_Companies_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("companies", context.Model.GetDefaultSchema());
    }

    [Fact]
    public void Company_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(Company))!;

        Assert.Equal("companies", entityType.GetTableName());
        Assert.Equal("companies", entityType.GetSchema());
    }

    [Fact]
    public void Company_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(Company))!;
        var pk = entityType.FindPrimaryKey()!;

        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void Company_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(Company))!;

        var violations = entityType
            .GetProperties()
            .Select(p => p.GetColumnName())
            .Where(name => name.Any(char.IsUpper))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Column names must be snake_case. Violations: {string.Join(", ", violations)}");
    }
}
