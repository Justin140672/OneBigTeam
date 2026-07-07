using System.Reflection;
using HR.Modules.DataImport;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class DataImportModuleArchitectureTests
{
    private static readonly Assembly ModuleAssembly = typeof(DataImportModule).Assembly;

    private static DataImportDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<DataImportDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        return new DataImportDbContext(options);
    }

    [Fact]
    public void DataImport_Module_Only_Exposes_Registration_Surface_As_Public()
    {
        var unexpected = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name is not "DataImportModule")
            .Where(t => t.Namespace?.Contains(".Migrations") is not true)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected public types in DataImport module: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void DataImport_DbContext_Is_Not_Public()
    {
        var dbContextType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "DataImportDbContext");

        Assert.False(dbContextType.IsPublic, "DataImportDbContext must be internal, not public.");
    }

    [Fact]
    public void DataImport_DbContext_Uses_DataImport_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("data_import", context.Model.GetDefaultSchema());
    }

    // ── ImportSession ────────────────────────────────────────────────────────────

    [Fact]
    public void ImportSession_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "ImportSession");
        Assert.False(entityType.IsPublic, "ImportSession entity must be internal, not public.");
    }

    [Fact]
    public void ImportSession_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(ImportSession))!;
        Assert.Equal("import_sessions", entityType.GetTableName());
        Assert.Equal("data_import", entityType.GetSchema());
    }

    [Fact]
    public void ImportSession_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(ImportSession))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void ImportSession_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(ImportSession))!);
    }

    // ── ImportRowError ───────────────────────────────────────────────────────────

    [Fact]
    public void ImportRowError_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "ImportRowError");
        Assert.False(entityType.IsPublic, "ImportRowError entity must be internal, not public.");
    }

    [Fact]
    public void ImportRowError_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(ImportRowError))!;
        Assert.Equal("import_row_errors", entityType.GetTableName());
        Assert.Equal("data_import", entityType.GetSchema());
    }

    [Fact]
    public void ImportRowError_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(ImportRowError))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void ImportRowError_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(ImportRowError))!);
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
