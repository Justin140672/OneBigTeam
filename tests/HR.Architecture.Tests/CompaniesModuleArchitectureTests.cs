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
            .Where(t => t.Namespace?.Contains(".Migrations") is not true)
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

    // ── OBT-REM-07: ProcessedStripeEvent entity ──────────────────────────

    private static Type ProcessedStripeEventType => ModuleAssembly
        .GetTypes()
        .Single(t => t.Name == "ProcessedStripeEvent");

    [Fact]
    public void ProcessedStripeEvent_Entity_Is_Not_Public()
        => Assert.False(ProcessedStripeEventType.IsPublic, "ProcessedStripeEvent must be internal.");

    [Fact]
    public void ProcessedStripeEvent_Maps_To_Expected_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(ProcessedStripeEventType)!;

        Assert.Equal("processed_stripe_events", entityType.GetTableName());
        Assert.Equal("companies", entityType.GetSchema());
    }

    [Fact]
    public void ProcessedStripeEvent_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var pk = context.Model.FindEntityType(ProcessedStripeEventType)!.FindPrimaryKey()!;

        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void ProcessedStripeEvent_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(ProcessedStripeEventType)!;

        var violations = entityType
            .GetProperties()
            .Select(p => p.GetColumnName())
            .Where(name => name.Any(char.IsUpper))
            .ToArray();

        Assert.True(violations.Length == 0, $"Violations: {string.Join(", ", violations)}");
    }

    [Fact]
    public void ProcessedStripeEvent_Has_Unique_Index_On_StripeEventId()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(ProcessedStripeEventType)!;

        var unique = entityType.GetIndexes().Single(i => i.IsUnique);
        Assert.Equal("ix_processed_stripe_events_stripe_event_id", unique.GetDatabaseName());
        Assert.Equal("StripeEventId", Assert.Single(unique.Properties).Name);
    }
}
