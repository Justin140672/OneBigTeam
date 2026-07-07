using System.Reflection;
using HR.Modules.Onboarding;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class OnboardingModuleArchitectureTests
{
    private static readonly Assembly ModuleAssembly = typeof(OnboardingModule).Assembly;

    private static OnboardingDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        return new OnboardingDbContext(options);
    }

    [Fact]
    public void Onboarding_Module_Only_Exposes_Registration_Surface_As_Public()
    {
        var unexpected = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name is not "OnboardingModule")
            .Where(t => t.Namespace?.Contains(".Migrations") is not true)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected public types in Onboarding module: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Onboarding_DbContext_Is_Not_Public()
    {
        var dbContextType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "OnboardingDbContext");

        Assert.False(dbContextType.IsPublic, "OnboardingDbContext must be internal, not public.");
    }

    [Fact]
    public void Onboarding_DbContext_Uses_Onboarding_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("onboarding", context.Model.GetDefaultSchema());
    }

    [Fact]
    public void OnboardingPlan_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "OnboardingPlan");
        Assert.False(entityType.IsPublic, "OnboardingPlan entity must be internal, not public.");
    }

    [Fact]
    public void OnboardingTaskTemplate_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "OnboardingTaskTemplate");
        Assert.False(entityType.IsPublic, "OnboardingTaskTemplate entity must be internal, not public.");
    }

    [Fact]
    public void OnboardingPlan_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(OnboardingPlan))!;
        Assert.Equal("onboarding_plans", entityType.GetTableName());
        Assert.Equal("onboarding", entityType.GetSchema());
    }

    [Fact]
    public void OnboardingTaskTemplate_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(OnboardingTaskTemplate))!;
        Assert.Equal("onboarding_task_templates", entityType.GetTableName());
        Assert.Equal("onboarding", entityType.GetSchema());
    }

    [Fact]
    public void OnboardingPlan_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(OnboardingPlan))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void OnboardingTaskTemplate_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(OnboardingTaskTemplate))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void OnboardingPlan_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(OnboardingPlan))!);
    }

    [Fact]
    public void OnboardingTaskTemplate_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(OnboardingTaskTemplate))!);
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
