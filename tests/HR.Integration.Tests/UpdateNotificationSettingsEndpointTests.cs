using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// See UpdateNotificationSettingsHandlerTests/UpdateNotificationSettingsValidatorTests/
/// CompanySettingsNotificationSettingsTests in HR.Modules.Companies.Tests for the equivalent
/// unit-level coverage of the same behaviour.
/// </summary>
[Collection("Integration")]
public class UpdateNotificationSettingsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUserId = new("ce000031-0000-0000-0000-000000000001");
    private static readonly Guid RecruiterOnlyUserId = new("ce000031-0000-0000-0000-000000000002");

    public UpdateNotificationSettingsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterOnlyUserId, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterOnlyUserId, SystemRoles.Employee);
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

    private static object ValidBody(int version = 1) => new
    {
        emailNotificationsEnabled = false,
        scheduledRemindersEnabled = false,
        version,
    };

    [Fact]
    public async Task Put_NotificationSettings_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/notification-settings", ValidBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_NotificationSettings_Succeeds_For_HrAdministrator_Role()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.PutAsJsonAsync($"/api/companies/{tenantId}/notification-settings", ValidBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<NotificationSettingsPayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.EmailNotificationsEnabled);
        Assert.False(payload.ScheduledRemindersEnabled);
    }

    [Fact]
    public async Task Put_NotificationSettings_Returns_Forbidden_For_Recruiter_Only_Role()
    {
        // Proves "the Recruiter role alone cannot change company-wide configuration": Recruiter
        // holds recruitment:manage but not hr-settings:manage.
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(RecruiterOnlyUserId, tenantId);

        var response = await client.PutAsJsonAsync($"/api/companies/{tenantId}/notification-settings", ValidBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_NotificationSettings_Returns_Conflict_When_Version_Is_Stale()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var firstResponse = await client.PutAsJsonAsync($"/api/companies/{tenantId}/notification-settings", ValidBody(version: 1));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await client.PutAsJsonAsync($"/api/companies/{tenantId}/notification-settings", ValidBody(version: 1));

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    private sealed record NotificationSettingsPayload(
        Guid CompanyId,
        bool EmailNotificationsEnabled,
        bool ScheduledRemindersEnabled,
        DateTimeOffset UpdatedAt,
        int Version);
}
