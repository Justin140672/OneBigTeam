using System.Reflection;
using HR.Modules.Documents;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class DocumentsModuleArchitectureTests
{
    private static readonly Assembly ModuleAssembly = typeof(DocumentsModule).Assembly;

    private static DocumentsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        return new DocumentsDbContext(options);
    }

    [Fact]
    public void Documents_Module_Only_Exposes_Registration_Surface_As_Public()
    {
        var unexpected = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name is not "DocumentsModule")
            .Where(t => t.Namespace?.Contains(".Migrations") is not true)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected public types in Documents module: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Documents_DbContext_Is_Not_Public()
    {
        var dbContextType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "DocumentsDbContext");

        Assert.False(dbContextType.IsPublic, "DocumentsDbContext must be internal, not public.");
    }

    [Fact]
    public void Document_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "Document");

        Assert.False(entityType.IsPublic, "Document entity must be internal, not public.");
    }

    [Fact]
    public void Documents_DbContext_Uses_Documents_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("documents", context.Model.GetDefaultSchema());
    }

    [Fact]
    public void Document_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(Document))!;

        Assert.Equal("documents", entityType.GetTableName());
        Assert.Equal("documents", entityType.GetSchema());
    }

    [Fact]
    public void Document_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(Document))!;
        var pk = entityType.FindPrimaryKey()!;

        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void Document_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(Document))!;

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
