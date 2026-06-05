using System.Reflection;
using HR.Modules.Identity;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class IdentityModuleArchitectureTests
{
    private static readonly Assembly ModuleAssembly = typeof(IdentityModule).Assembly;

    private static IdentityDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        return new IdentityDbContext(options);
    }

    [Fact]
    public void Identity_Module_Only_Exposes_Registration_Surface_As_Public()
    {
        var unexpected = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name is not "IdentityModule")
            .Where(t => t.Namespace?.Contains(".Migrations") is not true)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected public types in Identity module: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Identity_DbContext_Is_Not_Public()
    {
        var dbContextType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "IdentityDbContext");

        Assert.False(dbContextType.IsPublic, "IdentityDbContext must be internal, not public.");
    }

    [Fact]
    public void ApplicationUser_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "ApplicationUser");

        Assert.False(entityType.IsPublic, "ApplicationUser entity must be internal, not public.");
    }

    [Fact]
    public void Identity_DbContext_Uses_Identity_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("identity", context.Model.GetDefaultSchema());
    }

    [Fact]
    public void ApplicationUser_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(ApplicationUser))!;

        Assert.Equal("users", entityType.GetTableName());
        Assert.Equal("identity", entityType.GetSchema());
    }

    [Fact]
    public void ApplicationUser_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(ApplicationUser))!;
        var pk = entityType.FindPrimaryKey()!;

        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void ApplicationUser_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(ApplicationUser))!;

        var violations = entityType
            .GetProperties()
            .Select(p => p.GetColumnName())
            .Where(name => name.Any(char.IsUpper))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Column names must be snake_case. Violations: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Role_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(Role))!;

        Assert.Equal("roles", entityType.GetTableName());
        Assert.Equal("identity", entityType.GetSchema());
    }

    [Fact]
    public void UserRole_Entity_Has_Composite_Primary_Key()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(UserRole))!;
        var pk = entityType.FindPrimaryKey()!;

        Assert.Equal(2, pk.Properties.Count);
        Assert.Contains(pk.Properties, p => p.Name == nameof(UserRole.UserId));
        Assert.Contains(pk.Properties, p => p.Name == nameof(UserRole.RoleId));
    }
}
