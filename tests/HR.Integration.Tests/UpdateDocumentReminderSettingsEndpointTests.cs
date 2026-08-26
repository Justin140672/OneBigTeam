using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// See UpdateDocumentReminderSettingsHandlerTests/UpdateDocumentReminderSettingsValidatorTests/
/// CompanySettingsDocumentReminderSettingsTests in HR.Modules.Companies.Tests for the equivalent
/// unit-level coverage of the same behaviour.
/// </summary>
[Collection("Integration")]
public class UpdateDocumentReminderSettingsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUserId = new("ce000032-0000-0000-0000-000000000001");
    private static readonly Guid RecruiterOnlyUserId = new("ce000032-0000-0000-0000-000000000002");

    public UpdateDocumentReminderSettingsEndpointTests(ApiWebApplicationFactory factory)
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
        remindersEnabled = true,
        offsetDays1 = 60,
        offsetDays2 = 21,
        offsetDays3 = 3,
        version,
    };

    [Fact]
    public async Task Put_DocumentReminderSettings_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/document-reminder-settings", ValidBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_DocumentReminderSettings_Succeeds_For_HrAdministrator_Role()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{tenantId}/document-reminder-settings", ValidBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DocumentReminderSettingsPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.RemindersEnabled);
        Assert.Equal(60, payload.OffsetDays1);
        Assert.Equal(21, payload.OffsetDays2);
        Assert.Equal(3, payload.OffsetDays3);
    }

    [Fact]
    public async Task Put_DocumentReminderSettings_Returns_Forbidden_For_Recruiter_Only_Role()
    {
        // Proves "the Recruiter role alone cannot change company-wide configuration": Recruiter
        // holds recruitment:manage but not hr-settings:manage.
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(RecruiterOnlyUserId, tenantId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{tenantId}/document-reminder-settings", ValidBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_DocumentReminderSettings_Returns_UnprocessableEntity_When_Offsets_Are_Not_Strictly_Decreasing()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var body = new { remindersEnabled = true, offsetDays1 = 10, offsetDays2 = 30, offsetDays3 = 7, version = 1 };

        var response = await client.PutAsJsonAsync($"/api/companies/{tenantId}/document-reminder-settings", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_DocumentReminderSettings_Returns_UnprocessableEntity_When_Offsets_Have_Duplicates()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var body = new { remindersEnabled = true, offsetDays1 = 30, offsetDays2 = 30, offsetDays3 = 7, version = 1 };

        var response = await client.PutAsJsonAsync($"/api/companies/{tenantId}/document-reminder-settings", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_DocumentReminderSettings_Returns_UnprocessableEntity_When_An_Offset_Is_Not_Positive()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var body = new { remindersEnabled = true, offsetDays1 = 90, offsetDays2 = 30, offsetDays3 = 0, version = 1 };

        var response = await client.PutAsJsonAsync($"/api/companies/{tenantId}/document-reminder-settings", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_DocumentReminderSettings_Returns_UnprocessableEntity_When_Enabled_And_All_Offsets_Are_Null()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var body = new { remindersEnabled = true, offsetDays1 = (int?)null, offsetDays2 = (int?)null, offsetDays3 = (int?)null, version = 1 };

        var response = await client.PutAsJsonAsync($"/api/companies/{tenantId}/document-reminder-settings", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_DocumentReminderSettings_Returns_Conflict_When_Version_Is_Stale()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var firstResponse = await client.PutAsJsonAsync(
            $"/api/companies/{tenantId}/document-reminder-settings", ValidBody(version: 1));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await client.PutAsJsonAsync(
            $"/api/companies/{tenantId}/document-reminder-settings", ValidBody(version: 1));

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    private sealed record DocumentReminderSettingsPayload(
        Guid CompanyId,
        bool RemindersEnabled,
        int? OffsetDays1,
        int? OffsetDays2,
        int? OffsetDays3,
        DateTimeOffset UpdatedAt,
        int Version);
}
