using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListPositionProfilesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("22222222-0000-0000-0000-000000000001");

    public ListPositionProfilesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private static async Task<Guid> CreateDepartmentAsync(HttpClient client, Guid companyId, string name = "Engineering")
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = $"{name} {Guid.NewGuid():N}"
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        Assert.NotNull(payload);
        return payload!.Id;
    }

    private static async Task<Guid> CreateLocationAsync(HttpClient client, Guid companyId, string name = "Head Office")
    {
        var locationTypeResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/location-types", new
        {
            companyId,
            name = $"Office Type {Guid.NewGuid():N}"
        });
        locationTypeResponse.EnsureSuccessStatusCode();
        var locationType = await locationTypeResponse.Content.ReadFromJsonAsync<IdPayload>();
        Assert.NotNull(locationType);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new
        {
            companyId,
            name = $"{name} {Guid.NewGuid():N}",
            locationTypeId = locationType!.Id
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        Assert.NotNull(payload);
        return payload!.Id;
    }

    private static async Task<Guid> CreateLeavePolicyAsync(HttpClient client, Guid companyId, string name = "Standard Leave")
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-policies", new
        {
            companyId,
            name = $"{name} {Guid.NewGuid():N}",
            carryOverDays = 5,
            allowNegativeBalance = false
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        Assert.NotNull(payload);
        return payload!.Id;
    }

    private async Task<(Guid DepartmentId, Guid LocationId, Guid LeavePolicyId)> SeedReferenceDataAsync(HttpClient client, Guid companyId)
    {
        var departmentId = await CreateDepartmentAsync(client, companyId);
        var locationId = await CreateLocationAsync(client, companyId);
        var leavePolicyId = await CreateLeavePolicyAsync(client, companyId);
        return (departmentId, locationId, leavePolicyId);
    }

    [Fact]
    public async Task Get_PositionProfiles_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/position-profiles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_PositionProfiles_Returns_Empty_List_When_None_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/position-profiles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PositionProfilesListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_PositionProfiles_Returns_Created_Profiles_Alphabetically()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var create1 = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = "Software Engineer"
        });
        create1.EnsureSuccessStatusCode();

        var create2 = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = "Engineering Manager"
        });
        create2.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/position-profiles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PositionProfilesListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);
        Assert.Equal("Engineering Manager", payload.Items[0].Title);
        Assert.Equal("Software Engineer", payload.Items[1].Title);
    }

    [Fact]
    public async Task Get_PositionProfiles_Returns_NoticePeriodOverride_When_Set()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var create = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = "Software Engineer",
            noticePeriodUnitOverride = "Months",
            noticePeriodLengthOverride = 2
        });
        create.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/position-profiles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PositionProfilesListPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal("Months", item.NoticePeriodUnitOverride);
        Assert.Equal(2, item.NoticePeriodLengthOverride);
    }

    [Fact]
    public async Task Get_PositionProfiles_Scopes_To_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var clientA = AdminClient(companyA);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(clientA, companyA);
        var create = await clientA.PostAsJsonAsync($"/api/companies/{companyA}/position-profiles", new
        {
            companyId = companyA,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = "Analyst"
        });
        create.EnsureSuccessStatusCode();

        using var clientB = AdminClient(companyB);
        var response = await clientB.GetAsync($"/api/companies/{companyB}/position-profiles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PositionProfilesListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record PositionProfilesListPayload(IReadOnlyList<PositionProfileItem> Items);

    private sealed record PositionProfileItem(
        Guid Id,
        string? DepartmentName,
        string Title,
        string? Description,
        bool IsActive,
        string? NoticePeriodUnitOverride,
        int? NoticePeriodLengthOverride);
}
