using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class CreatePositionProfileEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeeeeeee-0000-0000-0000-000000000004");

    public CreatePositionProfileEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
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
    public async Task Post_PositionProfiles_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{Guid.NewGuid()}/position-profiles", new
        {
            title = "Software Developer"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_PositionProfiles_Creates_PositionProfile()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = "Software Developer",
            description = "Builds features"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<PositionProfilePayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal(departmentId, payload.DepartmentId);
        Assert.Equal(locationId, payload.LocationId);
        Assert.Equal(leavePolicyId, payload.DefaultLeavePolicyId);
        Assert.Equal("Software Developer", payload.Title);
        Assert.Equal("Builds features", payload.Description);
        Assert.True(payload.IsActive);
    }

    [Fact]
    public async Task Post_PositionProfiles_Creates_PositionProfile_With_Department()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = "Engineering Manager"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PositionProfilePayload>();
        Assert.NotNull(payload);
        Assert.Equal(departmentId, payload!.DepartmentId);
    }

    [Fact]
    public async Task Post_PositionProfiles_Returns_Conflict_For_Duplicate_Title()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var first = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = "Recruiter"
        });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = "Recruiter"
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_PositionProfiles_Returns_NotFound_For_Unknown_Department()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (_, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId = Guid.NewGuid(),
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = "Developer"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_PositionProfiles_Returns_NotFound_For_Unknown_Location()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, _, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId = Guid.NewGuid(),
            defaultLeavePolicyId = leavePolicyId,
            title = "Developer"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_PositionProfiles_Returns_NotFound_For_Unknown_DefaultLeavePolicyId()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, _) = await SeedReferenceDataAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = Guid.NewGuid(),
            title = "Developer"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_PositionProfiles_Returns_BadRequest_When_DepartmentId_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (_, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId = Guid.Empty,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = "Developer"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_PositionProfiles_Returns_BadRequest_When_LocationId_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, _, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId = Guid.Empty,
            defaultLeavePolicyId = leavePolicyId,
            title = "Developer"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_PositionProfiles_Returns_BadRequest_When_DefaultLeavePolicyId_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, _) = await SeedReferenceDataAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = Guid.Empty,
            title = "Developer"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_PositionProfiles_Creates_PositionProfile_With_NoticePeriodOverride()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = "Software Developer",
            noticePeriodUnitOverride = "Weeks",
            noticePeriodLengthOverride = 4
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PositionProfilePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Weeks", payload!.NoticePeriodUnitOverride);
        Assert.Equal(4, payload.NoticePeriodLengthOverride);
    }

    [Fact]
    public async Task Post_PositionProfiles_Returns_BadRequest_When_Only_NoticePeriodUnitOverride_Is_Set()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = "Developer",
            noticePeriodUnitOverride = "Weeks"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_PositionProfiles_Returns_BadRequest_When_Only_NoticePeriodLengthOverride_Is_Set()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = "Developer",
            noticePeriodLengthOverride = 4
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_PositionProfiles_Returns_BadRequest_When_NoticePeriodLengthOverride_Is_Not_Greater_Than_Zero()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = "Developer",
            noticePeriodUnitOverride = "Weeks",
            noticePeriodLengthOverride = 0
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record PositionProfilePayload(
        Guid Id,
        Guid CompanyId,
        Guid DepartmentId,
        Guid LocationId,
        string Title,
        string? Description,
        Guid DefaultLeavePolicyId,
        bool IsActive,
        DateTimeOffset CreatedAt,
        string? NoticePeriodUnitOverride,
        int? NoticePeriodLengthOverride);
}
