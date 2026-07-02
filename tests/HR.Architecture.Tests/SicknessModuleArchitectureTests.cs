using System.Reflection;
using HR.Modules.Sickness;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class SicknessModuleArchitectureTests
{
    private static readonly Assembly ModuleAssembly = typeof(SicknessModule).Assembly;

    private static SicknessDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<SicknessDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        return new SicknessDbContext(options);
    }

    [Fact]
    public void Sickness_Module_Only_Exposes_Registration_Surface_As_Public()
    {
        var unexpected = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name is not "SicknessModule")
            .Where(t => t.Namespace?.Contains(".Migrations") is not true)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected public types in Sickness module: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Sickness_DbContext_Is_Not_Public()
    {
        var dbContextType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "SicknessDbContext");

        Assert.False(dbContextType.IsPublic, "SicknessDbContext must be internal, not public.");
    }

    [Fact]
    public void Sickness_DbContext_Uses_Sickness_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("sickness", context.Model.GetDefaultSchema());
    }

    // ── SicknessCategory ─────────────────────────────────────────────────────────

    [Fact]
    public void SicknessCategory_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "SicknessCategory");
        Assert.False(entityType.IsPublic, "SicknessCategory entity must be internal, not public.");
    }

    [Fact]
    public void SicknessCategory_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(SicknessCategory))!;
        Assert.Equal("sickness_categories", entityType.GetTableName());
        Assert.Equal("sickness", entityType.GetSchema());
    }

    [Fact]
    public void SicknessCategory_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(SicknessCategory))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void SicknessCategory_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(SicknessCategory))!);
    }

    // ── SicknessRecord ───────────────────────────────────────────────────────────

    [Fact]
    public void SicknessRecord_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "SicknessRecord");
        Assert.False(entityType.IsPublic, "SicknessRecord entity must be internal, not public.");
    }

    [Fact]
    public void SicknessRecord_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(SicknessRecord))!;
        Assert.Equal("sickness_records", entityType.GetTableName());
        Assert.Equal("sickness", entityType.GetSchema());
    }

    [Fact]
    public void SicknessRecord_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(SicknessRecord))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void SicknessRecord_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(SicknessRecord))!);
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
