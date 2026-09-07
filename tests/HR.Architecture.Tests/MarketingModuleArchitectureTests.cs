using System.Reflection;

using HR.Modules.Marketing;
using HR.Modules.Marketing.Domain;
using HR.Modules.Marketing.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class MarketingModuleArchitectureTests
{
    private static readonly Assembly ModuleAssembly = typeof(MarketingModule).Assembly;

    private static MarketingDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<MarketingDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        return new MarketingDbContext(options);
    }

    [Fact]
    public void Marketing_Module_Only_Exposes_Registration_Surface_As_Public()
    {
        var unexpected = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name is not "MarketingModule")
            .Where(t => t.Namespace?.Contains(".Migrations") is not true)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected public types in Marketing module: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Marketing_DbContext_Is_Not_Public()
    {
        var dbContextType = ModuleAssembly.GetTypes().Single(t => t.Name == "MarketingDbContext");
        Assert.False(dbContextType.IsPublic, "MarketingDbContext must be internal, not public.");
    }

    [Fact]
    public void Marketing_DbContext_Uses_Marketing_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("marketing", context.Model.GetDefaultSchema());
    }

    [Theory]
    [InlineData("MarketingProduct")]
    [InlineData("MarketingFeature")]
    [InlineData("MarketingRoadmapItem")]
    public void Entity_Is_Internal(string entityName)
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == entityName);
        Assert.False(entityType.IsPublic, $"{entityName} entity must be internal, not public.");
    }

    [Theory]
    [InlineData(typeof(MarketingProduct), "marketing_products")]
    [InlineData(typeof(MarketingFeature), "marketing_features")]
    [InlineData(typeof(MarketingRoadmapItem), "marketing_roadmap_items")]
    public void Entity_Maps_To_Expected_Table_And_Schema(Type entityClrType, string tableName)
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(entityClrType)!;
        Assert.Equal(tableName, entityType.GetTableName());
        Assert.Equal("marketing", entityType.GetSchema());
    }

    [Theory]
    [InlineData(typeof(MarketingProduct))]
    [InlineData(typeof(MarketingFeature))]
    [InlineData(typeof(MarketingRoadmapItem))]
    public void Entity_Primary_Key_Is_A_Single_Guid(Type entityClrType)
    {
        using var context = BuildContext();
        var pk = context.Model.FindEntityType(entityClrType)!.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Theory]
    [InlineData(typeof(MarketingProduct))]
    [InlineData(typeof(MarketingFeature))]
    [InlineData(typeof(MarketingRoadmapItem))]
    public void Entity_All_Column_Names_Are_snake_case(Type entityClrType)
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(entityClrType)!;
        var violations = entityType
            .GetProperties()
            .Select(p => p.GetColumnName())
            .Where(name => name.Any(char.IsUpper))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Column names must be snake_case in {entityType.Name}. Violations: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Marketing_Module_Assembly_Does_Not_Reference_Other_Modules()
    {
        var forbidden = ModuleAssembly
            .GetReferencedAssemblies()
            .Where(a => a.Name is not null
                && a.Name.StartsWith("HR.Modules.", StringComparison.Ordinal)
                && a.Name != "HR.Modules.Marketing")
            .Select(a => a.Name!)
            .ToArray();

        Assert.True(forbidden.Length == 0, $"Marketing module references other modules: {string.Join(", ", forbidden)}");
    }
}
