using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetUserAuditHistoryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("aaaaaaa9-0000-0000-0000-000000000001");

    public GetUserAuditHistoryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId, Guid? userId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, (userId ?? AdminUser).ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Get_UserAuditHistory_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/companies/{companyId}/users/{Guid.NewGuid()}/audit-history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_UserAuditHistory_Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeUserId, SystemRoles.Employee);

        using var client = AuthenticatedClient(companyId, employeeUserId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users/{Guid.NewGuid()}/audit-history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_UserAuditHistory_Returns_Empty_For_Employee_With_No_Events()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        // IAM-01: the employee must actually belong to the route company or the handler now
        // (correctly) returns NotFound rather than an empty history for an arbitrary/nonexistent id.
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "No", "Events");

        var response = await client.GetAsync($"/api/companies/{companyId}/users/{employeeId}/audit-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_UserAuditHistory_Includes_Invite_Event_After_Invite_Created()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Audit", "Person");
        var roleId = await IdentityUserAdminTestHelpers.SeedRoleAsync(_factory, $"Role-{Guid.NewGuid():N}");

        var inviteResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/invite-user",
            new { companyId, employeeId, email = "audit@test.com", roleIds = new[] { roleId } });
        inviteResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/users/{employeeId}/audit-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.EventType == "user.invited");
    }

    [Fact]
    public async Task Get_UserAuditHistory_Returns_NotFound_When_Employee_Belongs_To_Another_Company()
    {
        // IAM-01 regression: caller's own companyId is in the route (passes tenant middleware),
        // but the target employeeId belongs to a different company — must 404, not leak history.
        var ownCompanyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var otherCompanyEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, otherCompanyId, "Other", "Company");

        using var client = AuthenticatedClient(ownCompanyId);

        var response = await client.GetAsync($"/api/companies/{ownCompanyId}/users/{otherCompanyEmployeeId}/audit-history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record HistoryPayload(IReadOnlyList<HistoryItemPayload> Items);

    private sealed record HistoryItemPayload(DateTimeOffset OccurredAt, string EventType, string Summary, string PerformedBy);
}
