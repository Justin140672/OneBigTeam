using System.Reflection;
using HR.Modules.Notifications;
using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class NotificationsModuleArchitectureTests
{
    private static readonly Assembly ModuleAssembly = typeof(NotificationsModule).Assembly;

    private static NotificationsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        return new NotificationsDbContext(options);
    }

    [Fact]
    public void Notifications_Module_Only_Exposes_Registration_Surface_As_Public()
    {
        var unexpected = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name is not "NotificationsModule")
            .Where(t => t.Namespace?.Contains(".Migrations") is not true)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected public types in Notifications module: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Notifications_DbContext_Is_Not_Public()
    {
        var dbContextType = ModuleAssembly.GetTypes().Single(t => t.Name == "NotificationsDbContext");

        Assert.False(dbContextType.IsPublic, "NotificationsDbContext must be internal, not public.");
    }

    [Fact]
    public void Notifications_DbContext_Uses_Notifications_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("notifications", context.Model.GetDefaultSchema());
    }

    [Fact]
    public void Notifications_Module_Entity_Types_Are_Not_Public()
    {
        using var context = BuildContext();

        var publicEntityClrTypes = context.Model
            .GetEntityTypes()
            .Select(e => e.ClrType)
            .Where(t => t.IsPublic)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            publicEntityClrTypes.Length == 0,
            $"Notifications entity types must be internal, not public: {string.Join(", ", publicEntityClrTypes)}");
    }

    [Fact]
    public void Notifications_Configuration_And_Handler_Types_Are_Not_Public()
    {
        var leaked = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name.EndsWith("Configuration", StringComparison.Ordinal)
                        || t.Name.EndsWith("Handler", StringComparison.Ordinal)
                        || t.Name.EndsWith("Writer", StringComparison.Ordinal))
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(leaked.Length == 0, $"Internal types leaked as public: {string.Join(", ", leaked)}");
    }

    [Fact]
    public void AdministrativeAlert_Maps_To_Expected_Snake_Case_Table()
    {
        using var context = BuildContext();

        var entityType = context.Model.GetEntityTypes()
            .Single(e => e.ClrType.Name == "AdministrativeAlert");

        Assert.Equal("administrative_alerts", entityType.GetTableName());
    }

    [Fact]
    public void AdministrativeAlert_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();

        var entityType = context.Model.GetEntityTypes()
            .Single(e => e.ClrType.Name == "AdministrativeAlert");

        var pk = Assert.Single(entityType.FindPrimaryKey()!.Properties);
        Assert.Equal(typeof(Guid), pk.ClrType);
    }

    [Fact]
    public void Notifications_Module_Entity_Columns_Are_snake_case()
    {
        using var context = BuildContext();

        foreach (var entityType in context.Model.GetEntityTypes())
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

    [Fact]
    public void Notifications_Module_Does_Not_Reference_Other_Modules()
    {
        var forbiddenReferences = ModuleAssembly
            .GetReferencedAssemblies()
            .Where(reference =>
                reference.Name is not null &&
                reference.Name.StartsWith("HR.Modules.", StringComparison.Ordinal) &&
                !string.Equals(reference.Name, ModuleAssembly.GetName().Name, StringComparison.Ordinal) &&
                !reference.Name.EndsWith(".Contracts", StringComparison.Ordinal))
            .Select(reference => reference.Name!)
            .ToArray();

        Assert.True(
            forbiddenReferences.Length == 0,
            $"Notifications module references other modules: {string.Join(", ", forbiddenReferences)}");
    }
}
