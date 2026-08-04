using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateLeaveTypeEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000002-0000-0000-0000-000000000001");

    public CreateLeaveTypeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    [Fact]
    public async Task Post_LeaveTypes_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{Guid.NewGuid()}/leave-types", new { name = "Annual" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_LeaveTypes_Creates_LeaveType()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-types", new
        {
            companyId,
            name = "Annual Leave",
            code = "ANNUAL",
            defaultEntitlementDays = 25,
            accrualMethod = "Monthly",
            behaviour = "Standard"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<LeaveTypePayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Annual Leave", payload.Name);
        Assert.Equal("ANNUAL", payload.Code);
        Assert.Equal(25, payload.DefaultEntitlementDays);
        Assert.True(payload.IsActive);
    }

    [Fact]
    public async Task Post_LeaveTypes_Returns_Conflict_For_Duplicate_Code()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var first = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-types", new
        {
            companyId,
            name = "Annual Leave",
            code = "ANNUAL",
            defaultEntitlementDays = 25,
            accrualMethod = "Monthly",
            behaviour = "Standard"
        });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-types", new
        {
            companyId,
            name = "Annual Holiday",
            code = "annual",
            defaultEntitlementDays = 28,
            accrualMethod = "Monthly",
            behaviour = "Standard"
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    private sealed record LeaveTypePayload(
        Guid Id, Guid CompanyId, string Name, string Code,
        int DefaultEntitlementDays, string AccrualMethod, string Behaviour,
        bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
