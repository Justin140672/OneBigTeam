using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListLeaveTypesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000001-0000-0000-0000-000000000001");

    public ListLeaveTypesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.Employee))
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
    public async Task Get_LeaveTypes_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/leave-types");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeaveTypes_Returns_Empty_List_When_None_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/leave-types");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_LeaveTypes_Returns_Created_Types()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var create1 = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-types", new
        {
            companyId,
            name = "Annual Leave",
            code = "ANNUAL",
            defaultEntitlementDays = 25,
            accrualMethod = "Monthly",
            behaviour = "Standard"
        });
        create1.EnsureSuccessStatusCode();

        var create2 = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-types", new
        {
            companyId,
            name = "Sick Leave",
            code = "SICK",
            defaultEntitlementDays = 10,
            accrualMethod = "None",
            behaviour = "Sickness"
        });
        create2.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/leave-types");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);
        Assert.Equal("Annual Leave", payload.Items[0].Name);
        Assert.Equal("Sick Leave", payload.Items[1].Name);
        Assert.All(payload.Items, item => Assert.True(item.IsActive));
    }

    [Fact]
    public async Task Get_LeaveTypes_Scopes_To_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var clientA = await AdminClient(companyA);
        var create = await clientA.PostAsJsonAsync($"/api/companies/{companyA}/leave-types", new
        {
            companyId = companyA,
            name = "Annual Leave",
            code = "ANNUAL",
            defaultEntitlementDays = 25,
            accrualMethod = "Monthly",
            behaviour = "Standard"
        });
        create.EnsureSuccessStatusCode();

        using var clientB = await AdminClient(companyB);
        var response = await clientB.GetAsync($"/api/companies/{companyB}/leave-types");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record ListPayload(IReadOnlyList<LeaveTypeItem> Items);
    private sealed record LeaveTypeItem(Guid Id, Guid CompanyId, string Name, string Code, int DefaultEntitlementDays, bool IsActive);
}
