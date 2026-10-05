using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateWorkEmailSettingsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUserId = new("7e0a1001-0000-0000-0000-000000000001");
    private static readonly Guid EmployeeOnlyUserId = new("7e0a1001-0000-0000-0000-000000000002");

    public UpdateWorkEmailSettingsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeOnlyUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, tenantId);
        return client;
    }

    private static object Body(
        bool enabled = true,
        string? primaryDomain = "example.com",
        string convention = "FirstNameDotLastName",
        int version = 1) => new
    {
        suggestionsEnabled = enabled,
        primaryDomain,
        namingConvention = convention,
        version,
    };

    private static string Url(Guid companyId) => $"/api/companies/{companyId}/work-email-settings";

    [Fact]
    public async Task Put_WorkEmailSettings_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(Url(Guid.NewGuid()), Body());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_WorkEmailSettings_Returns_Forbidden_For_Non_HrAdministrator()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(EmployeeOnlyUserId, tenantId);

        var response = await client.PutAsJsonAsync(Url(tenantId), Body());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_WorkEmailSettings_Persists_And_Normalises_Values_For_HrAdministrator()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.PutAsJsonAsync(Url(tenantId), Body(
            primaryDomain: " @Example.CO.uk ",
            convention: "FirstInitialDotLastName"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SettingsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(tenantId, payload!.CompanyId);
        Assert.True(payload.SuggestionsEnabled);
        Assert.Equal("example.co.uk", payload.PrimaryDomain);
        Assert.Equal("FirstInitialDotLastName", payload.NamingConvention);
        Assert.Equal(2, payload.Version);

        var reloaded = await client.GetFromJsonAsync<SettingsPayload>(Url(tenantId));
        Assert.Equal("example.co.uk", reloaded!.PrimaryDomain);
        Assert.Equal("FirstInitialDotLastName", reloaded.NamingConvention);
        Assert.Equal(2, reloaded.Version);
    }

    [Fact]
    public async Task Put_WorkEmailSettings_Allows_Suggestions_Disabled_And_Keeps_The_Primary_Domain()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.PutAsJsonAsync(Url(tenantId), Body(enabled: false, primaryDomain: "example.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SettingsPayload>();
        Assert.False(payload!.SuggestionsEnabled);
        Assert.Equal("example.com", payload.PrimaryDomain);
    }

    [Theory]
    [InlineData("notadomain")]
    [InlineData("user@example.com")]
    [InlineData("exa mple.com")]
    public async Task Put_WorkEmailSettings_Rejects_Invalid_Primary_Domain(string primaryDomain)
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.PutAsJsonAsync(Url(tenantId), Body(primaryDomain: primaryDomain));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Put_WorkEmailSettings_Rejects_Missing_Primary_Domain_Whether_Enabled_Or_Not(string? primaryDomain)
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var enabled = await client.PutAsJsonAsync(Url(tenantId), Body(enabled: true, primaryDomain: primaryDomain));
        var disabled = await client.PutAsJsonAsync(Url(tenantId), Body(enabled: false, primaryDomain: primaryDomain));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, enabled.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, disabled.StatusCode);
    }

    [Fact]
    public async Task Put_WorkEmailSettings_Cannot_Clear_An_Already_Saved_Primary_Domain()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);
        var saved = await client.PutAsJsonAsync(Url(tenantId), Body(version: 1));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var cleared = await client.PutAsJsonAsync(Url(tenantId), Body(primaryDomain: null, version: 2));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, cleared.StatusCode);
        var current = await client.GetFromJsonAsync<SettingsPayload>(Url(tenantId));
        Assert.Equal("example.com", current!.PrimaryDomain);
        Assert.Equal(2, current.Version);
    }

    [Fact]
    public async Task Put_WorkEmailSettings_Rejects_Unknown_Naming_Convention()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.PutAsJsonAsync(Url(tenantId), Body(convention: "99"));

        Assert.False(response.IsSuccessStatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_WorkEmailSettings_Returns_Conflict_When_Version_Is_Stale()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var first = await client.PutAsJsonAsync(Url(tenantId), Body(version: 1));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PutAsJsonAsync(Url(tenantId), Body(primaryDomain: "other.com", version: 1));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var current = await client.GetFromJsonAsync<SettingsPayload>(Url(tenantId));
        Assert.Equal("example.com", current!.PrimaryDomain);
    }

    [Fact]
    public async Task Put_WorkEmailSettings_Writes_Audit_Event()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.PutAsJsonAsync(Url(tenantId), Body());
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        HR.Infrastructure.Persistence.AuditEvent? auditRecord = null;
        for (var attempt = 0; attempt < 50 && auditRecord is null; attempt++)
        {
            auditRecord = await auditDb.AuditEvents
                .AsNoTracking()
                .Where(e => e.CompanyId == tenantId && e.EventType == "work-email-settings.updated")
                .FirstOrDefaultAsync();
            if (auditRecord is null)
                await Task.Delay(200);
        }

        Assert.NotNull(auditRecord);
        Assert.Equal("CompanySettings", auditRecord!.EntityType);
        Assert.Equal(tenantId, auditRecord.EntityId);
    }

    private sealed record SettingsPayload(
        Guid CompanyId,
        bool SuggestionsEnabled,
        string? PrimaryDomain,
        string NamingConvention,
        int Version);
}
