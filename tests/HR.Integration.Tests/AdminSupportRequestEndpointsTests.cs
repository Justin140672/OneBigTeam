using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 17: platform-support admin routes (/api/admin/companies/{companyId}/support/requests...),
/// gated purely by "platform:admin" (see PlatformAdministratorTestHelpers), reusing the same
/// handlers as the tenant "support:manage" routes. See GetCustomerDetailsEndpointTests and
/// PlatformAdministratorTestHelpers for the DI/persona pattern this follows, and
/// UpdateSupportRequestStatusConcurrencyEndpointTests for the concurrency/notification pattern
/// mirrored here for the admin route.
/// </summary>
[Collection("Integration")]
public class AdminSupportRequestEndpointsTests
{
    private readonly ApiWebApplicationFactory _factory;

    public AdminSupportRequestEndpointsTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static MultipartFormDataContent BuildSubmission(Guid companyId, string title) => new()
    {
        { new StringContent(companyId.ToString()), "CompanyId" },
        { new StringContent("AskQuestion"), "Type" },
        { new StringContent(title), "Title" },
        { new StringContent("Some description of the issue."), "Description" },
        { new StringContent("Low"), "Priority" },
        { new StringContent("false"), "IncludeDiagnostics" },
    };

    private async Task<HttpClient> TenantHrAdminClientAsync(Guid companyId)
    {
        var userId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private async Task<(Guid CompanyId, Guid Id, int Version)> SeedSupportRequestAsync()
    {
        var companyId = Guid.NewGuid();
        using var tenantClient = await TenantHrAdminClientAsync(companyId);

        var created = await tenantClient.PostAsync(
            $"/api/companies/{companyId}/support/requests",
            BuildSubmission(companyId, "Admin route test issue"));
        created.EnsureSuccessStatusCode();
        var payload = await created.Content.ReadFromJsonAsync<SubmitPayload>();

        var detail = await tenantClient.GetAsync($"/api/companies/{companyId}/support/requests/{payload!.Id}");
        detail.EnsureSuccessStatusCode();
        var detailPayload = await detail.Content.ReadFromJsonAsync<DetailPayload>();

        return (companyId, payload.Id, detailPayload!.Version);
    }

    // ---------------------------------------------------------------------
    // 1-2: Allow-listed platform administrator can list/get across tenants.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task PlatformAdministrator_Can_List_SupportRequests_For_Any_Company()
    {
        var (companyId, id, _) = await SeedSupportRequestAsync();
        var (_, adminEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff);

        using var adminClient = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), adminEmail);

        var response = await adminClient.GetAsync($"/api/admin/companies/{companyId}/support/requests");
        response.EnsureSuccessStatusCode();

        var list = await response.Content.ReadFromJsonAsync<List<ListItemPayload>>();
        Assert.NotNull(list);
        Assert.Contains(list!, r => r.Id == id);
    }

    [Fact]
    public async Task PlatformAdministrator_Can_Get_SupportRequest_Detail_For_Any_Company()
    {
        var (companyId, id, _) = await SeedSupportRequestAsync();
        var (_, adminEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff);

        using var adminClient = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), adminEmail);

        var response = await adminClient.GetAsync($"/api/admin/companies/{companyId}/support/requests/{id}");
        response.EnsureSuccessStatusCode();

        var detail = await response.Content.ReadFromJsonAsync<DetailPayload>();
        Assert.NotNull(detail);
        Assert.Equal(id, detail!.Id);
    }

    // ---------------------------------------------------------------------
    // 3: Disabled/unknown platform administrators are forbidden on all three routes.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Disabled_PlatformAdministrator_Is_Forbidden_On_All_Admin_Routes()
    {
        var (companyId, id, version) = await SeedSupportRequestAsync();
        var (_, disabledEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff, isEnabled: false);

        using var adminClient = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), disabledEmail);

        await AssertAllThreeForbiddenAsync(adminClient, companyId, id, version);
    }

    [Fact]
    public async Task Unknown_Caller_Is_Forbidden_On_All_Admin_Routes()
    {
        var (companyId, id, version) = await SeedSupportRequestAsync();

        using var unknownClient = PlatformAdministratorTestHelpers.ClientFor(
            _factory, Guid.NewGuid(), "unknown-caller@example.com");

        await AssertAllThreeForbiddenAsync(unknownClient, companyId, id, version);
    }

    // ---------------------------------------------------------------------
    // 4: An ordinary authenticated tenant user (no platform-admin row) is forbidden.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Ordinary_TenantUser_Is_Forbidden_On_All_Admin_Routes()
    {
        var (companyId, id, version) = await SeedSupportRequestAsync();

        var tenantUserId = Guid.NewGuid();
        using var tenantClient = _factory.CreateClient();
        tenantClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, tenantUserId.ToString());
        tenantClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, tenantUserId, SystemRoles.Employee, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, tenantUserId, SystemRoles.HrAdministrator, companyId);

        await AssertAllThreeForbiddenAsync(tenantClient, companyId, id, version);
    }

    private async Task AssertAllThreeForbiddenAsync(HttpClient client, Guid companyId, Guid id, int version)
    {
        var list = await client.GetAsync($"/api/admin/companies/{companyId}/support/requests");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var get = await client.GetAsync($"/api/admin/companies/{companyId}/support/requests/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);

        var put = await client.PutAsJsonAsync(
            $"/api/admin/companies/{companyId}/support/requests/{id}/status",
            new { companyId, id, status = "UnderReview", expectedVersion = version });
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
    }

    // ---------------------------------------------------------------------
    // 5: Cross-company isolation via the admin Get route.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task PlatformAdministrator_Get_Returns_NotFound_When_Id_Belongs_To_Different_Company()
    {
        var (_, id, _) = await SeedSupportRequestAsync();
        var otherCompanyId = Guid.NewGuid();

        var (_, adminEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff);
        using var adminClient = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), adminEmail);

        var response = await adminClient.GetAsync($"/api/admin/companies/{otherCompanyId}/support/requests/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------
    // 6-7: Update status via the admin route — success + notification fan-out, and 409 conflict.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task PlatformAdministrator_Update_Status_With_Correct_Version_Succeeds_And_Fires_Notifications()
    {
        var (companyId, id, version) = await SeedSupportRequestAsync();
        var (_, adminEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff);
        using var adminClient = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), adminEmail);

        var response = await adminClient.PutAsJsonAsync(
            $"/api/admin/companies/{companyId}/support/requests/{id}/status",
            new { companyId, id, status = "UnderReview", expectedVersion = version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.Equal(version + 1, payload!.Version);
        Assert.Equal("UnderReview", payload.Status);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var notificationCount = await db.Notifications
            .Where(n => n.CompanyId == companyId && n.SourceEntityId == id)
            .CountAsync();
        Assert.True(notificationCount > 0);
    }

    [Fact]
    public async Task PlatformAdministrator_Update_Status_With_Stale_Version_Returns_409_And_Does_Not_Change_State()
    {
        var (companyId, id, version) = await SeedSupportRequestAsync();
        var (_, adminEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff);
        using var adminClient = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), adminEmail);

        var legitimate = await adminClient.PutAsJsonAsync(
            $"/api/admin/companies/{companyId}/support/requests/{id}/status",
            new { companyId, id, status = "UnderReview", expectedVersion = version });
        legitimate.EnsureSuccessStatusCode();

        int notificationCountAfterLegitimateChange;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            notificationCountAfterLegitimateChange = await db.Notifications
                .Where(n => n.CompanyId == companyId && n.SourceEntityId == id)
                .CountAsync();
        }
        Assert.True(notificationCountAfterLegitimateChange > 0);

        var stale = await adminClient.PutAsJsonAsync(
            $"/api/admin/companies/{companyId}/support/requests/{id}/status",
            new { companyId, id, status = "Planned", expectedVersion = version });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = await stale.Content.ReadFromJsonAsync<ConcurrencyProblemPayload>();
        Assert.NotNull(problem);
        Assert.Equal("concurrency", problem!.Code);

        var current = await adminClient.GetAsync($"/api/admin/companies/{companyId}/support/requests/{id}");
        current.EnsureSuccessStatusCode();
        var currentDetail = await current.Content.ReadFromJsonAsync<DetailPayload>();
        Assert.Equal("UnderReview", currentDetail!.Status);
        Assert.Equal(version + 1, currentDetail.Version);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var notificationCountAfterRejectedSave = await db.Notifications
                .Where(n => n.CompanyId == companyId && n.SourceEntityId == id)
                .CountAsync();
            Assert.Equal(notificationCountAfterLegitimateChange, notificationCountAfterRejectedSave);
        }
    }

    private sealed record SubmitPayload(Guid Id, string ReferenceNumber);
    private sealed record ListItemPayload(Guid Id, string Status);
    private sealed record DetailPayload(Guid Id, string Status, int Version);
    private sealed record StatusPayload(Guid Id, string Status, DateTimeOffset UpdatedAt, int Version);
    private sealed record ConcurrencyProblemPayload(string Error, string Code);
}
