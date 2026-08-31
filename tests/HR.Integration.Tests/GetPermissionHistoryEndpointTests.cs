using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetPermissionHistoryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetPermissionHistoryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId, Guid? userId = null, Guid? role = null)
    {
        var client = _factory.CreateClient();
        var effectiveUserId = userId ?? Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, effectiveUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, effectiveUserId, role ?? SystemRoles.HrAdministrator, companyId);
        return client;
    }

    [Fact]
    public async Task Get_PermissionHistory_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/users/permission-history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_PermissionHistory_Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId, role: SystemRoles.Employee);

        var response = await client.GetAsync($"/api/companies/{companyId}/users/permission-history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_PermissionHistory_Returns_UnprocessableEntity_For_Invalid_PageSize()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users/permission-history?pageSize=0");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_PermissionHistory_Returns_UnprocessableEntity_When_ToDate_Is_Before_FromDate()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/users/permission-history?fromDate=2026-06-10&toDate=2026-06-01");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // NOTE: A true end-to-end "role change produces a visible history entry" happy-path assertion
    // is intentionally covered at the unit level (GetPermissionHistoryHandlerTests, against a fake
    // IAuditHistoryReader) rather than here. Audit events only become queryable after
    // AuditPendingItemPromotionJob promotes them from the pending staging table — that job runs
    // once a minute via Hangfire (see AuditJobRegistrar) in production, and, independently, this
    // branch's uncommitted AUD-01..04 audit-outbox/actor-type migration chain does not currently
    // apply cleanly against a fresh Testcontainers Postgres instance (verified: both invoking the
    // promotion job directly and inserting an AuditEvent row directly via AuditDbContext fail —
    // the former trips AuditDbContext's AUD-02 append-only guard on its own internal status
    // update, the latter hits a missing "actor_type" column even after an explicit
    // Database.MigrateAsync()). Both are pre-existing issues in already-uncommitted, unrelated
    // work (see `git status` for HR.Infrastructure/Migrations/*AuditOutbox*) — out of scope for
    // IAM-08 to fix. This integration test instead exercises the full authenticated request/
    // response/wiring path (routing, policy, validation, empty-history mapping).
    [Fact]
    public async Task Get_PermissionHistory_Returns_OK_With_An_Empty_History_When_Nothing_Has_Changed()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users/permission-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
        Assert.Equal(0, payload.TotalCount);
    }

    private sealed record HistoryItemPayload(DateTimeOffset OccurredAt, string EventType, string Summary, string PerformedBy);
    private sealed record HistoryPayload(List<HistoryItemPayload> Items, int TotalCount, int Page, int PageSize);
}
