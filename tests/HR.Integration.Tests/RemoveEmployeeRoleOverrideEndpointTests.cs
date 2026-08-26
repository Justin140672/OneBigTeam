using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class RemoveEmployeeRoleOverrideEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("aaaaaaa5-0000-0000-0000-000000000001");

    public RemoveEmployeeRoleOverrideEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId, Guid? userId = null, Guid? role = null)
    {
        var client = _factory.CreateClient();
        var effectiveUserId = userId ?? AdminUser;
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, effectiveUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, effectiveUserId, role ?? SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private async Task AddOverrideAsync(HttpClient client, Guid companyId, Guid userId, Guid roleId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/users/{userId}/role-overrides",
            new { companyId, userId, roleId, overrideType = 1, reason = "Seed override" });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Delete_RemoveEmployeeRoleOverride_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        var response = await client.DeleteAsync($"/api/companies/{companyId}/users/{userId}/role-overrides/{roleId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemoveEmployeeRoleOverride_Returns_NotFound_When_User_Belongs_To_Another_Company()
    {
        var ownCompanyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(ownCompanyId);
        var otherCompanyEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, otherCompanyId, "Other", "Company");
        var otherCompanyUserId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(
            _factory, otherCompanyEmployeeId, $"othercompany.{Guid.NewGuid():N}@test.com");

        var response = await client.DeleteAsync(
            $"/api/companies/{ownCompanyId}/users/{otherCompanyUserId}/role-overrides/{SystemRoles.Manager}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemoveEmployeeRoleOverride_Returns_NotFound_When_No_Override_Exists()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var userId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(_factory, employeeId, $"no-override.{Guid.NewGuid():N}@test.com");

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/users/{userId}/role-overrides/{SystemRoles.Manager}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemoveEmployeeRoleOverride_Removes_Override_On_Happy_Path()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId);
        var userId = await IdentityUserAdminTestHelpers.SeedApplicationUserAsync(_factory, employeeId, $"happy-path.{Guid.NewGuid():N}@test.com");
        await AddOverrideAsync(client, companyId, userId, SystemRoles.Manager);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/users/{userId}/role-overrides/{SystemRoles.Manager}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/users/{userId}/role-overrides");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var payload = await getResponse.Content.ReadFromJsonAsync<ListResponsePayload>();
        Assert.NotNull(payload);
        Assert.DoesNotContain(payload!.Overrides, o => o.RoleId == SystemRoles.Manager);
    }

    private sealed record OverrideItemPayload(Guid RoleId, string OverrideType, string Reason, DateTimeOffset? ExpiresAt);

    private sealed record ListResponsePayload(List<OverrideItemPayload> Overrides);
}
