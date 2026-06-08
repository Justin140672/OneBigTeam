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

    [Fact]
    public void UserProfile_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(UserProfile))!;

        Assert.Equal("user_profiles", entityType.GetTableName());
        Assert.Equal("identity", entityType.GetSchema());
    }

    [Fact]
    public void UserProfile_Entity_Contains_Supabase_User_Id_Column()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(UserProfile))!;
        var supabaseUserIdProperty = entityType.FindProperty(nameof(UserProfile.SupabaseAuthUserId));

        Assert.NotNull(supabaseUserIdProperty);
        Assert.Equal("supabase_auth_user_id", supabaseUserIdProperty!.GetColumnName());
    }

    // ── UserPosition boundary tests ──────────────────────────────────────────

    [Fact]
    public void UserPosition_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "UserPosition");

        Assert.False(entityType.IsPublic, "UserPosition must be internal — it is an implementation detail of the Identity module.");
    }

    [Fact]
    public void Position_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "Position");

        Assert.False(entityType.IsPublic, "Position must be internal — it is an implementation detail of the Identity module.");
    }

    [Fact]
    public void PositionRole_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly
            .GetTypes()
            .Single(t => t.Name == "PositionRole");

        Assert.False(entityType.IsPublic, "PositionRole must be internal — it is an implementation detail of the Identity module.");
    }

    [Fact]
    public void UserPosition_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(UserPosition))!;

        Assert.Equal("user_positions", entityType.GetTableName());
        Assert.Equal("identity", entityType.GetSchema());
    }

    [Fact]
    public void UserPosition_Entity_Has_Composite_Primary_Key()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(UserPosition))!;
        var pk = entityType.FindPrimaryKey()!;

        Assert.Equal(2, pk.Properties.Count);
        Assert.Contains(pk.Properties, p => p.Name == nameof(UserPosition.UserId));
        Assert.Contains(pk.Properties, p => p.Name == nameof(UserPosition.PositionId));
    }

    [Fact]
    public void UserPosition_Entity_ExpiresAt_Is_Nullable()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(UserPosition))!;
        var property = entityType.FindProperty(nameof(UserPosition.ExpiresAt))!;

        Assert.False(property.IsNullable is false, "ExpiresAt must be nullable to support open-ended position assignments.");
    }

    [Fact]
    public void Position_Entity_Has_Tenant_And_NormalizedName_Unique_Index()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(Position))!;

        var uniqueIndex = entityType
            .GetIndexes()
            .SingleOrDefault(i =>
                i.IsUnique &&
                i.Properties.Count == 2 &&
                i.Properties.Any(p => p.Name == nameof(Position.TenantId)) &&
                i.Properties.Any(p => p.Name == nameof(Position.NormalizedName)));

        Assert.NotNull(uniqueIndex);
    }

    [Fact]
    public void No_External_Assembly_Directly_References_UserPosition_Type()
    {
        var externalAssemblies = new[]
        {
            typeof(HR.Modules.Companies.CompaniesModule).Assembly,
            typeof(HR.Modules.Employees.EmployeesModule).Assembly,
        };

        var userPositionTypeName = "HR.Modules.Identity.Domain.UserPosition";

        var violations = externalAssemblies
            .SelectMany(asm => asm.GetTypes())
            .SelectMany(t => t.GetMembers())
            .OfType<System.Reflection.MethodBase>()
            .SelectMany(m =>
            {
                try { return m.GetMethodBody()?.LocalVariables.Select(v => v.LocalType) ?? []; }
                catch { return []; }
            })
            .Where(t => t?.FullName == userPositionTypeName)
            .Select(t => t!.FullName!)
            .Distinct()
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"External modules directly reference UserPosition, bypassing the Identity module boundary: {string.Join(", ", violations)}");
    }
}
