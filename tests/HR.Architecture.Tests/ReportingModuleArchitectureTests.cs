using System.Reflection;
using HR.Modules.Reporting;
using HR.Modules.Reporting.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class ReportingModuleArchitectureTests
{
    private static readonly Assembly ModuleAssembly = typeof(ReportingModule).Assembly;

    private static ReportingDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        return new ReportingDbContext(options);
    }

    [Fact]
    public void Reporting_Module_Only_Exposes_Registration_Surface_As_Public()
    {
        var unexpected = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name is not "ReportingModule")
            .Where(t => t.Namespace?.Contains(".Migrations") is not true)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected public types in Reporting module: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Reporting_DbContext_Is_Not_Public()
    {
        var dbContextType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "ReportingDbContext");

        Assert.False(dbContextType.IsPublic, "ReportingDbContext must be internal, not public.");
    }

    [Fact]
    public void Reporting_DbContext_Uses_Reporting_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("reporting", context.Model.GetDefaultSchema());
    }

    [Fact]
    public void Reporting_Module_Entity_Types_Are_Not_Public()
    {
        // No persisted entities exist yet in this phase, but if/when EF entities are added
        // to ReportingDbContext they must remain internal like every other module.
        using var context = BuildContext();

        var publicEntityClrTypes = context.Model
            .GetEntityTypes()
            .Select(e => e.ClrType)
            .Where(t => t.IsPublic)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            publicEntityClrTypes.Length == 0,
            $"Reporting entity types must be internal, not public: {string.Join(", ", publicEntityClrTypes)}");
    }

    [Fact]
    public void Reporting_Module_Entity_Columns_Are_snake_case()
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
    public void Reporting_Module_Does_Not_Reference_Other_Modules()
    {
        var forbiddenReferences = ModuleAssembly
            .GetReferencedAssemblies()
            .Where(reference =>
                reference.Name is not null &&
                reference.Name.StartsWith("HR.Modules.", StringComparison.Ordinal) &&
                !string.Equals(reference.Name, ModuleAssembly.GetName().Name, StringComparison.Ordinal))
            .Select(reference => reference.Name!)
            .ToArray();

        Assert.True(
            forbiddenReferences.Length == 0,
            $"Reporting module references other modules: {string.Join(", ", forbiddenReferences)}");
    }
}
