using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class DeactivateLeaveTypeEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000004-0000-0000-0000-000000000001");

    public DeactivateLeaveTypeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Delete_LeaveType_Deactivates_Successfully()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-types", new
        {
            companyId, name = "Annual Leave", code = "ANNUAL",
            defaultEntitlementDays = 25, accrualMethod = "Monthly", behaviour = "Standard"
        });
        created.EnsureSuccessStatusCode();
        var payload = await created.Content.ReadFromJsonAsync<LeaveTypePayload>();

        var response = await client.DeleteAsync($"/api/companies/{companyId}/leave-types/{payload!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_LeaveType_Returns_NotFound_For_Unknown_Id()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.DeleteAsync($"/api/companies/{companyId}/leave-types/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_LeaveType_Returns_BadRequest_When_Already_Inactive()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-types", new
        {
            companyId, name = "Annual Leave", code = "ANNUAL",
            defaultEntitlementDays = 25, accrualMethod = "Monthly", behaviour = "Standard"
        });
        created.EnsureSuccessStatusCode();
        var payload = await created.Content.ReadFromJsonAsync<LeaveTypePayload>();

        await client.DeleteAsync($"/api/companies/{companyId}/leave-types/{payload!.Id}");

        var second = await client.DeleteAsync($"/api/companies/{companyId}/leave-types/{payload.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    private sealed record LeaveTypePayload(Guid Id, Guid CompanyId, string Name, string Code, bool IsActive);
}
