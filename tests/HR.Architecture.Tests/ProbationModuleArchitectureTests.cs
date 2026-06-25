using System.Reflection;
using HR.Modules.Probation;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class ProbationModuleArchitectureTests
{
    private static readonly Assembly ModuleAssembly = typeof(ProbationModule).Assembly;

    private static ProbationDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<ProbationDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        return new ProbationDbContext(options);
    }

    [Fact]
    public void Probation_Module_Only_Exposes_Registration_Surface_As_Public()
    {
        var unexpected = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name is not "ProbationModule")
            .Where(t => t.Namespace?.Contains(".Migrations") is not true)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected public types in Probation module: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Probation_DbContext_Is_Not_Public()
    {
        var dbContextType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "ProbationDbContext");

        Assert.False(dbContextType.IsPublic, "ProbationDbContext must be internal, not public.");
    }

    [Fact]
    public void Probation_DbContext_Uses_Probation_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("probation", context.Model.GetDefaultSchema());
    }

    [Fact]
    public void ProbationRecord_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "ProbationRecord");
        Assert.False(entityType.IsPublic, "ProbationRecord entity must be internal, not public.");
    }

    [Fact]
    public void ProbationRecord_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(ProbationRecord))!;
        Assert.Equal("probation_records", entityType.GetTableName());
        Assert.Equal("probation", entityType.GetSchema());
    }

    [Fact]
    public void ProbationRecord_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(ProbationRecord))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void ProbationRecord_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(ProbationRecord))!);
    }

    private static void AssertSnakeCase(Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType)
    {
        var violations = entityType
            .GetProperties()
            .Select(p => p.GetColumnName())
            .Where(name => name.Any(char.IsUpper))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Column names must be snake_case in {entityType.Name}. Violations: {string.Join(", ", violations)}");
    }
}
