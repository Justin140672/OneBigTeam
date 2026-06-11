using System.Reflection;
using HR.Modules.Leave;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class LeaveModuleArchitectureTests
{
    private static readonly Assembly ModuleAssembly = typeof(LeaveModule).Assembly;

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        return new LeaveDbContext(options);
    }

    [Fact]
    public void Leave_Module_Only_Exposes_Registration_Surface_As_Public()
    {
        var unexpected = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name is not "LeaveModule")
            .Where(t => t.Namespace?.Contains(".Migrations") is not true)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected public types in Leave module: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Leave_DbContext_Is_Not_Public()
    {
        var dbContextType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "LeaveDbContext");

        Assert.False(dbContextType.IsPublic, "LeaveDbContext must be internal, not public.");
    }

    [Fact]
    public void LeaveRequest_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "LeaveRequest");

        Assert.False(entityType.IsPublic, "LeaveRequest entity must be internal, not public.");
    }

    [Fact]
    public void Leave_DbContext_Uses_Leave_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("leave", context.Model.GetDefaultSchema());
    }

    [Fact]
    public void LeaveRequest_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(LeaveRequest))!;

        Assert.Equal("leave_requests", entityType.GetTableName());
        Assert.Equal("leave", entityType.GetSchema());
    }

    [Fact]
    public void LeaveRequest_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(LeaveRequest))!;
        var pk = entityType.FindPrimaryKey()!;

        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void LeaveRequest_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(LeaveRequest))!;

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
