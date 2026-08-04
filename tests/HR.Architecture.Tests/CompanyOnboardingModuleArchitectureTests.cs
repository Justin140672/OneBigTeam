using System.Reflection;
using HR.Modules.CompanyOnboarding;
using HR.Modules.CompanyOnboarding.Domain;
using HR.Modules.CompanyOnboarding.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class CompanyOnboardingModuleArchitectureTests
{
    private static readonly Assembly ModuleAssembly = typeof(CompanyOnboardingModule).Assembly;

    private static CompanyOnboardingDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompanyOnboardingDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        return new CompanyOnboardingDbContext(options);
    }

    [Fact]
    public void CompanyOnboarding_Module_Only_Exposes_Registration_Surface_As_Public()
    {
        var unexpected = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name is not "CompanyOnboardingModule")
            .Where(t => t.Namespace?.Contains(".Migrations") is not true)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected public types in CompanyOnboarding module: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void CompanyOnboarding_DbContext_Is_Not_Public()
    {
        var dbContextType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "CompanyOnboardingDbContext");

        Assert.False(dbContextType.IsPublic, "CompanyOnboardingDbContext must be internal, not public.");
    }

    [Fact]
    public void CompanyOnboarding_DbContext_Uses_CompanyOnboarding_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("company_onboarding", context.Model.GetDefaultSchema());
    }

    // ── CompanyOnboardingProgress ───────────────────────────────────────────────

    [Fact]
    public void CompanyOnboardingProgress_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "CompanyOnboardingProgress");
        Assert.False(entityType.IsPublic, "CompanyOnboardingProgress entity must be internal, not public.");
    }

    [Fact]
    public void CompanyOnboardingProgress_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(CompanyOnboardingProgress))!;
        Assert.Equal("progress", entityType.GetTableName());
        Assert.Equal("company_onboarding", entityType.GetSchema());
    }

    [Fact]
    public void CompanyOnboardingProgress_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(CompanyOnboardingProgress))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void CompanyOnboardingProgress_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(CompanyOnboardingProgress))!);
    }

    // ── CompanyOnboardingTaskCompletion ─────────────────────────────────────────

    [Fact]
    public void CompanyOnboardingTaskCompletion_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "CompanyOnboardingTaskCompletion");
        Assert.False(entityType.IsPublic, "CompanyOnboardingTaskCompletion entity must be internal, not public.");
    }

    [Fact]
    public void CompanyOnboardingTaskCompletion_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(CompanyOnboardingTaskCompletion))!;
        Assert.Equal("task_completions", entityType.GetTableName());
        Assert.Equal("company_onboarding", entityType.GetSchema());
    }

    [Fact]
    public void CompanyOnboardingTaskCompletion_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(CompanyOnboardingTaskCompletion))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void CompanyOnboardingTaskCompletion_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(CompanyOnboardingTaskCompletion))!);
    }

    // ── Module isolation ─────────────────────────────────────────────────────────

    [Fact]
    public void CompanyOnboarding_Module_References_No_Other_HR_Module()
    {
        var referencedModuleAssemblies = ModuleAssembly
            .GetReferencedAssemblies()
            .Where(a => a.Name is not null && a.Name.StartsWith("HR.Modules.", StringComparison.Ordinal))
            .Where(a => a.Name != "HR.Modules.CompanyOnboarding")
            .Select(a => a.Name)
            .ToArray();

        Assert.True(
            referencedModuleAssemblies.Length == 0,
            $"CompanyOnboarding module must not reference other HR.Modules.* assemblies directly. Found: {string.Join(", ", referencedModuleAssemblies!)}");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

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
