using System.Reflection;
using HR.Modules.Offboarding;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class OffboardingModuleArchitectureTests
{
    private static readonly Assembly ModuleAssembly = typeof(OffboardingModule).Assembly;

    private static OffboardingDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<OffboardingDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        return new OffboardingDbContext(options);
    }

    [Fact]
    public void Offboarding_Module_Only_Exposes_Registration_Surface_As_Public()
    {
        var unexpected = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name is not "OffboardingModule")
            .Where(t => t.Namespace?.Contains(".Migrations") is not true)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected public types in Offboarding module: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Offboarding_DbContext_Is_Not_Public()
    {
        var dbContextType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "OffboardingDbContext");

        Assert.False(dbContextType.IsPublic, "OffboardingDbContext must be internal, not public.");
    }

    [Fact]
    public void Offboarding_DbContext_Uses_Offboarding_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("offboarding", context.Model.GetDefaultSchema());
    }

    [Fact]
    public void OffboardingPlan_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "OffboardingPlan");
        Assert.False(entityType.IsPublic, "OffboardingPlan entity must be internal, not public.");
    }

    [Fact]
    public void OffboardingTask_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "OffboardingTask");
        Assert.False(entityType.IsPublic, "OffboardingTask entity must be internal, not public.");
    }

    [Fact]
    public void OffboardingPlan_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(OffboardingPlan))!;
        Assert.Equal("offboarding_plans", entityType.GetTableName());
        Assert.Equal("offboarding", entityType.GetSchema());
    }

    [Fact]
    public void OffboardingTask_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(OffboardingTask))!;
        Assert.Equal("offboarding_tasks", entityType.GetTableName());
        Assert.Equal("offboarding", entityType.GetSchema());
    }

    [Fact]
    public void OffboardingPlan_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(OffboardingPlan))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void OffboardingTask_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(OffboardingTask))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void OffboardingPlan_Entity_Has_CompanyId_Column()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(OffboardingPlan))!;
        var property = entityType.FindProperty("CompanyId");
        Assert.NotNull(property);
        Assert.Equal(typeof(Guid), property!.ClrType);
        Assert.False(property.IsNullable);
    }

    [Fact]
    public void OffboardingTask_Entity_Has_CompanyId_Column()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(OffboardingTask))!;
        var property = entityType.FindProperty("CompanyId");
        Assert.NotNull(property);
        Assert.Equal(typeof(Guid), property!.ClrType);
        Assert.False(property.IsNullable);
    }

    [Fact]
    public void OffboardingPlan_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(OffboardingPlan))!);
    }

    [Fact]
    public void OffboardingTask_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(OffboardingTask))!);
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
