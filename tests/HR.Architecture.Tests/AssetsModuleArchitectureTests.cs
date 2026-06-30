using System.Reflection;
using HR.Modules.Assets;
using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class AssetsModuleArchitectureTests
{
    private static readonly Assembly ModuleAssembly = typeof(AssetsModule).Assembly;

    private static AssetsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        return new AssetsDbContext(options);
    }

    [Fact]
    public void Assets_Module_Only_Exposes_Registration_Surface_As_Public()
    {
        var unexpected = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name is not "AssetsModule")
            .Where(t => t.Namespace?.Contains(".Migrations") is not true)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected public types in Assets module: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Assets_DbContext_Is_Not_Public()
    {
        var dbContextType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "AssetsDbContext");

        Assert.False(dbContextType.IsPublic, "AssetsDbContext must be internal, not public.");
    }

    [Fact]
    public void Assets_DbContext_Uses_Assets_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("assets", context.Model.GetDefaultSchema());
    }

    // ── Asset ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Asset_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "Asset");
        Assert.False(entityType.IsPublic, "Asset entity must be internal, not public.");
    }

    [Fact]
    public void Asset_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(Asset))!;
        Assert.Equal("assets", entityType.GetTableName());
        Assert.Equal("assets", entityType.GetSchema());
    }

    [Fact]
    public void Asset_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(Asset))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void Asset_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(Asset))!);
    }

    // ── AssetCategory ────────────────────────────────────────────────────────────

    [Fact]
    public void AssetCategory_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "AssetCategory");
        Assert.False(entityType.IsPublic, "AssetCategory entity must be internal, not public.");
    }

    [Fact]
    public void AssetCategory_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(AssetCategory))!;
        Assert.Equal("asset_categories", entityType.GetTableName());
        Assert.Equal("assets", entityType.GetSchema());
    }

    [Fact]
    public void AssetCategory_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(AssetCategory))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void AssetCategory_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(AssetCategory))!);
    }

    // ── AssetAssignment ──────────────────────────────────────────────────────────

    [Fact]
    public void AssetAssignment_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "AssetAssignment");
        Assert.False(entityType.IsPublic, "AssetAssignment entity must be internal, not public.");
    }

    [Fact]
    public void AssetAssignment_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(AssetAssignment))!;
        Assert.Equal("asset_assignments", entityType.GetTableName());
        Assert.Equal("assets", entityType.GetSchema());
    }

    [Fact]
    public void AssetAssignment_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(AssetAssignment))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void AssetAssignment_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(AssetAssignment))!);
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
