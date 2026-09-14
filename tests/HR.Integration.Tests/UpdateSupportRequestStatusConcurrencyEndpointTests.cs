using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

// Ticket 15 (optimistic concurrency rollout): SupportRequest.Version coverage for
// PUT .../support/requests/{id}/status, against the real Postgres-backed ApiWebApplicationFactory.
// Follows UpdateAssetCategoryConcurrencyEndpointTests for the two-client racing pattern, and
// UpdateSupportRequestStatusEndpointTests for support-specific auth/seeding helpers.
[Collection("Integration")]
public class UpdateSupportRequestStatusConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = Guid.Parse("60000000-0000-0000-0000-000000000010");

    public UpdateSupportRequestStatusConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.Employee, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);
        return client;
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

    private async Task<(HttpClient Client, Guid CompanyId, Guid Id, int Version)> CreateRequestAsync()
    {
        var companyId = Guid.NewGuid();
        var client = await AdminClient(companyId);

        var created = await client.PostAsync($"/api/companies/{companyId}/support/requests", BuildSubmission(companyId, "Concurrency test issue"));
        created.EnsureSuccessStatusCode();
        var payload = await created.Content.ReadFromJsonAsync<SubmitPayload>();
        var detail = await GetDetailAsync(client, companyId, payload!.Id);

        return (client, companyId, payload.Id, detail.Version);
    }

    private static async Task<DetailPayload> GetDetailAsync(HttpClient client, Guid companyId, Guid id)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/support/requests/{id}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DetailPayload>())!;
    }

    [Fact]
    public async Task Two_Admins_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var (client, companyId, id, version) = await CreateRequestAsync();

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/support/requests/{id}/status",
            new { companyId, id, status = "UnderReview", expectedVersion = version });
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        var editorAPayload = await editorA.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.Equal(version + 1, editorAPayload!.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/support/requests/{id}/status",
            new { companyId, id, status = "Planned", expectedVersion = version });

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);

        var current = await GetDetailAsync(client, companyId, id);
        Assert.Equal("UnderReview", current.Status);
        Assert.Equal(version + 1, current.Version);
    }

    [Fact]
    public async Task Put_SupportRequestStatus_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, id, version) = await CreateRequestAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/support/requests/{id}/status",
            new { companyId, id, status = "UnderReview", expectedVersion = version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.Equal(version + 1, payload!.Version);
        Assert.Equal("UnderReview", payload.Status);
    }

    [Fact]
    public async Task Put_SupportRequestStatus_With_Stale_ExpectedVersion_Returns_409_And_Does_Not_Modify_Database()
    {
        var (client, companyId, id, version) = await CreateRequestAsync();

        // Someone else legitimately advances the version first.
        var legitimate = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/support/requests/{id}/status",
            new { companyId, id, status = "UnderReview", expectedVersion = version });
        legitimate.EnsureSuccessStatusCode();

        // A second caller still believes the old version is current.
        var stale = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/support/requests/{id}/status",
            new { companyId, id, status = "Planned", expectedVersion = version });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var current = await GetDetailAsync(client, companyId, id);
        Assert.Equal("UnderReview", current.Status);
        Assert.Equal(version + 1, current.Version);
    }

    [Fact]
    public async Task Put_SupportRequestStatus_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var (client, companyId, id, _) = await CreateRequestAsync();
        var before = await GetDetailAsync(client, companyId, id);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/support/requests/{id}/status",
            new { companyId, id, status = "UnderReview" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var after = await GetDetailAsync(client, companyId, id);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Version, after.Version);
    }

    [Fact]
    public async Task Rejected_Stale_Save_Does_Not_Create_Or_Remove_Any_Notification()
    {
        var (client, companyId, id, version) = await CreateRequestAsync();

        // Legitimate change (version -> version+1) triggers one notification per HR admin.
        var legitimate = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/support/requests/{id}/status",
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

        // Stale write is rejected with 409 — must not touch notifications at all.
        var stale = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/support/requests/{id}/status",
            new { companyId, id, status = "Planned", expectedVersion = version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

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
    private sealed record StatusPayload(Guid Id, string Status, DateTimeOffset UpdatedAt, int Version);
    private sealed record DetailPayload(Guid Id, string Status, int Version);
}
