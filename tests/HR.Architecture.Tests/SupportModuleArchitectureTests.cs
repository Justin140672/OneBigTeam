using System.Reflection;
using HR.Modules.Support;
using HR.Modules.Support.Domain;
using HR.Modules.Support.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class SupportModuleArchitectureTests
{
    private static readonly Assembly ModuleAssembly = typeof(SupportModule).Assembly;

    private static SupportDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<SupportDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        return new SupportDbContext(options);
    }

    [Fact]
    public void Support_Module_Only_Exposes_Registration_Surface_As_Public()
    {
        var unexpected = ModuleAssembly
            .GetExportedTypes()
            .Where(t => t.Name is not "SupportModule")
            .Where(t => t.Namespace?.Contains(".Migrations") is not true)
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Unexpected public types in Support module: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Support_DbContext_Is_Not_Public()
    {
        var dbContextType = ModuleAssembly.GetTypes().Single(t => t.Name == "SupportDbContext");
        Assert.False(dbContextType.IsPublic, "SupportDbContext must be internal, not public.");
    }

    [Fact]
    public void Support_DbContext_Uses_Support_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("support", context.Model.GetDefaultSchema());
    }

    // ── SupportRequest ───────────────────────────────────────────────────────────

    [Fact]
    public void SupportRequest_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "SupportRequest");
        Assert.False(entityType.IsPublic, "SupportRequest entity must be internal, not public.");
    }

    [Fact]
    public void SupportRequest_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(SupportRequest))!;
        Assert.Equal("support_requests", entityType.GetTableName());
        Assert.Equal("support", entityType.GetSchema());
    }

    [Fact]
    public void SupportRequest_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(SupportRequest))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void SupportRequest_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(SupportRequest))!);
    }

    // ── SupportAttachment ────────────────────────────────────────────────────────

    [Fact]
    public void SupportAttachment_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "SupportAttachment");
        Assert.False(entityType.IsPublic, "SupportAttachment entity must be internal, not public.");
    }

    [Fact]
    public void SupportAttachment_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(SupportAttachment))!;
        Assert.Equal("support_attachments", entityType.GetTableName());
        Assert.Equal("support", entityType.GetSchema());
    }

    [Fact]
    public void SupportAttachment_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(SupportAttachment))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void SupportAttachment_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(SupportAttachment))!);
    }

    // ── SupportResponse ──────────────────────────────────────────────────────────

    [Fact]
    public void SupportResponse_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "SupportResponse");
        Assert.False(entityType.IsPublic, "SupportResponse entity must be internal, not public.");
    }

    [Fact]
    public void SupportResponse_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(SupportResponse))!;
        Assert.Equal("support_responses", entityType.GetTableName());
        Assert.Equal("support", entityType.GetSchema());
    }

    [Fact]
    public void SupportResponse_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(SupportResponse))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void SupportResponse_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(SupportResponse))!);
    }

    // ── SupportResponseAttachment ────────────────────────────────────────────────

    [Fact]
    public void SupportResponseAttachment_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "SupportResponseAttachment");
        Assert.False(entityType.IsPublic, "SupportResponseAttachment entity must be internal, not public.");
    }

    [Fact]
    public void SupportResponseAttachment_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(SupportResponseAttachment))!;
        Assert.Equal("support_response_attachments", entityType.GetTableName());
        Assert.Equal("support", entityType.GetSchema());
    }

    [Fact]
    public void SupportResponseAttachment_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(SupportResponseAttachment))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void SupportResponseAttachment_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(SupportResponseAttachment))!);
    }

    // ── SupportNotificationAttempt ───────────────────────────────────────────────

    [Fact]
    public void SupportNotificationAttempt_Entity_Is_Not_Public()
    {
        var entityType = ModuleAssembly.GetTypes().Single(t => t.Name == "SupportNotificationAttempt");
        Assert.False(entityType.IsPublic, "SupportNotificationAttempt entity must be internal, not public.");
    }

    [Fact]
    public void SupportNotificationAttempt_Entity_Maps_To_Correct_Table_And_Schema()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(SupportNotificationAttempt))!;
        Assert.Equal("support_notification_attempts", entityType.GetTableName());
        Assert.Equal("support", entityType.GetSchema());
    }

    [Fact]
    public void SupportNotificationAttempt_Entity_Primary_Key_Is_Guid()
    {
        using var context = BuildContext();
        var entityType = context.Model.FindEntityType(typeof(SupportNotificationAttempt))!;
        var pk = entityType.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(typeof(Guid), pk.Properties[0].ClrType);
    }

    [Fact]
    public void SupportNotificationAttempt_Entity_All_Columns_Are_snake_case()
    {
        using var context = BuildContext();
        AssertSnakeCase(context.Model.FindEntityType(typeof(SupportNotificationAttempt))!);
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
