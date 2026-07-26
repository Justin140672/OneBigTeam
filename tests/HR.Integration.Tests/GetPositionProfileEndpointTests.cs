using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetPositionProfileEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("33333333-0000-0000-0000-000000000001");

    public GetPositionProfileEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_PositionProfile_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/position-profiles/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_PositionProfile_Returns_PositionProfile_For_Authenticated_Request()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = "Test Engineer"
        });
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<PositionProfilePayload>();
        Assert.NotNull(created);

        var response = await client.GetAsync($"/api/companies/{companyId}/position-profiles/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PositionProfilePayload>();
        Assert.NotNull(payload);
        Assert.Equal(created.Id, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Test Engineer", payload.Title);
        Assert.True(payload.IsActive);
    }

    [Fact]
    public async Task Get_PositionProfile_Returns_NoticePeriodOverride_When_Set()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = "Test Engineer",
            noticePeriodUnitOverride = "Weeks",
            noticePeriodLengthOverride = 4
        });
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<PositionProfilePayload>();
        Assert.NotNull(created);

        var response = await client.GetAsync($"/api/companies/{companyId}/position-profiles/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PositionProfilePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Weeks", payload!.NoticePeriodUnitOverride);
        Assert.Equal(4, payload.NoticePeriodLengthOverride);
    }

    [Fact]
    public async Task Get_PositionProfile_Returns_NotFound_For_Unknown_Id()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/position-profiles/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_PositionProfile_Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var client = AdminClient(companyA);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyA);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyA}/position-profiles", new
        {
            companyId = companyA,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = "Analyst"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<PositionProfilePayload>();
        Assert.NotNull(created);

        // Authenticated as companyA but route targets companyB — middleware blocks it.
        var response = await client.GetAsync($"/api/companies/{companyB}/position-profiles/{created!.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record PositionProfilePayload(
        Guid Id,
        Guid CompanyId,
        Guid? DepartmentId,
        string Title,
        string? Description,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string? NoticePeriodUnitOverride,
        int? NoticePeriodLengthOverride);
}
