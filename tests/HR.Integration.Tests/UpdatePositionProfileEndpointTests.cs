using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdatePositionProfileEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeeeeeee-0000-0000-0000-000000000005");

    public UpdatePositionProfileEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<PositionProfilePayload> CreatePositionProfileAsync(
        HttpClient client, Guid companyId, Guid departmentId, Guid locationId, Guid leavePolicyId, string title)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<PositionProfilePayload>();
        Assert.NotNull(payload);
        return payload!;
    }

    [Fact]
    public async Task Put_PositionProfile_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/position-profiles/{Guid.NewGuid()}", new
        {
            title = "Some Title"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_PositionProfile_Updates_Profile()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var created = await CreatePositionProfileAsync(client, companyId, departmentId, locationId, leavePolicyId, "Original Title");

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{created.Id}", new
            {
                companyId,
                id = created.Id,
                departmentId,
                locationId,
                defaultLeavePolicyId = leavePolicyId,
                title = "Updated Title",
                description = "Now with description"
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var payload = await updateResponse.Content.ReadFromJsonAsync<UpdatedPositionProfilePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Updated Title", payload!.Title);
        Assert.Equal("Now with description", payload.Description);
        Assert.Equal(departmentId, payload.DepartmentId);
        Assert.Equal(locationId, payload.LocationId);
        Assert.Equal(leavePolicyId, payload.DefaultLeavePolicyId);
    }

    [Fact]
    public async Task Put_PositionProfile_Returns_NotFound_For_Unknown_Id()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{Guid.NewGuid()}", new
            {
                companyId,
                id = Guid.NewGuid(),
                departmentId,
                locationId,
                defaultLeavePolicyId = leavePolicyId,
                title = "Whatever"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_PositionProfile_Returns_Conflict_For_Duplicate_Title()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);

        await CreatePositionProfileAsync(client, companyId, departmentId, locationId, leavePolicyId, "Profile A");
        var profileB = await CreatePositionProfileAsync(client, companyId, departmentId, locationId, leavePolicyId, "Profile B");

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileB.Id}", new
            {
                companyId,
                id = profileB.Id,
                departmentId,
                locationId,
                defaultLeavePolicyId = leavePolicyId,
                title = "Profile A"
            });

        Assert.Equal(HttpStatusCode.Conflict, updateResponse.StatusCode);
    }

    [Fact]
    public async Task Put_PositionProfile_Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        using var client = AuthenticatedClient(companyA);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyA);

        var created = await CreatePositionProfileAsync(client, companyA, departmentId, locationId, leavePolicyId, "Designer");

        // Authenticated as companyA but route targets companyB — middleware blocks it.
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyB}/position-profiles/{created.Id}", new
            {
                companyId = companyB,
                id = created.Id,
                departmentId,
                locationId,
                defaultLeavePolicyId = leavePolicyId,
                title = "Designer"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_PositionProfile_Returns_BadRequest_When_DepartmentId_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);
        var created = await CreatePositionProfileAsync(client, companyId, departmentId, locationId, leavePolicyId, "Original Title");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{created.Id}", new
            {
                companyId,
                id = created.Id,
                departmentId = Guid.Empty,
                locationId,
                defaultLeavePolicyId = leavePolicyId,
                title = "Original Title"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_PositionProfile_Returns_BadRequest_When_LocationId_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);
        var created = await CreatePositionProfileAsync(client, companyId, departmentId, locationId, leavePolicyId, "Original Title");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{created.Id}", new
            {
                companyId,
                id = created.Id,
                departmentId,
                locationId = Guid.Empty,
                defaultLeavePolicyId = leavePolicyId,
                title = "Original Title"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_PositionProfile_Returns_BadRequest_When_DefaultLeavePolicyId_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);
        var created = await CreatePositionProfileAsync(client, companyId, departmentId, locationId, leavePolicyId, "Original Title");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{created.Id}", new
            {
                companyId,
                id = created.Id,
                departmentId,
                locationId,
                defaultLeavePolicyId = Guid.Empty,
                title = "Original Title"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_PositionProfile_Updates_NoticePeriodOverride()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);
        var created = await CreatePositionProfileAsync(client, companyId, departmentId, locationId, leavePolicyId, "Original Title");

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{created.Id}", new
            {
                companyId,
                id = created.Id,
                departmentId,
                locationId,
                defaultLeavePolicyId = leavePolicyId,
                title = "Original Title",
                noticePeriodUnitOverride = "Months",
                noticePeriodLengthOverride = 2
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var payload = await updateResponse.Content.ReadFromJsonAsync<UpdatedPositionProfilePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Months", payload!.NoticePeriodUnitOverride);
        Assert.Equal(2, payload.NoticePeriodLengthOverride);
    }

    [Fact]
    public async Task Put_PositionProfile_Clears_NoticePeriodOverride_Back_To_Null()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);
        var created = await CreatePositionProfileAsync(client, companyId, departmentId, locationId, leavePolicyId, "Original Title");

        var firstUpdate = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{created.Id}", new
            {
                companyId,
                id = created.Id,
                departmentId,
                locationId,
                defaultLeavePolicyId = leavePolicyId,
                title = "Original Title",
                noticePeriodUnitOverride = "Weeks",
                noticePeriodLengthOverride = 4
            });
        firstUpdate.EnsureSuccessStatusCode();

        var clearResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{created.Id}", new
            {
                companyId,
                id = created.Id,
                departmentId,
                locationId,
                defaultLeavePolicyId = leavePolicyId,
                title = "Original Title"
            });

        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);

        var payload = await clearResponse.Content.ReadFromJsonAsync<UpdatedPositionProfilePayload>();
        Assert.NotNull(payload);
        Assert.Null(payload!.NoticePeriodUnitOverride);
        Assert.Null(payload.NoticePeriodLengthOverride);
    }

    [Fact]
    public async Task Put_PositionProfile_Returns_BadRequest_When_Only_NoticePeriodUnitOverride_Is_Set()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);
        var created = await CreatePositionProfileAsync(client, companyId, departmentId, locationId, leavePolicyId, "Original Title");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{created.Id}", new
            {
                companyId,
                id = created.Id,
                departmentId,
                locationId,
                defaultLeavePolicyId = leavePolicyId,
                title = "Original Title",
                noticePeriodUnitOverride = "Weeks"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_PositionProfile_Returns_BadRequest_When_Only_NoticePeriodLengthOverride_Is_Set()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (departmentId, locationId, leavePolicyId) = await SeedReferenceDataAsync(client, companyId);
        var created = await CreatePositionProfileAsync(client, companyId, departmentId, locationId, leavePolicyId, "Original Title");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{created.Id}", new
            {
                companyId,
                id = created.Id,
                departmentId,
                locationId,
                defaultLeavePolicyId = leavePolicyId,
                title = "Original Title",
                noticePeriodLengthOverride = 4
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
        DateTimeOffset CreatedAt);

    private sealed record UpdatedPositionProfilePayload(
        Guid Id,
        Guid CompanyId,
        Guid DepartmentId,
        Guid LocationId,
        string Title,
        string? Description,
        Guid DefaultLeavePolicyId,
        bool IsActive,
        DateTimeOffset UpdatedAt,
        string? NoticePeriodUnitOverride,
        int? NoticePeriodLengthOverride);
}
