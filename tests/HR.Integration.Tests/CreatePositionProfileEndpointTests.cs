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

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            title = "Software Developer",
            description = "Builds features",
            isManagerial = false
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<PositionProfilePayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Software Developer", payload.Title);
        Assert.Equal("Builds features", payload.Description);
        Assert.False(payload.IsManagerial);
        Assert.True(payload.IsActive);
    }

    [Fact]
    public async Task Post_PositionProfiles_Creates_Managerial_Profile_With_Department()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var deptResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = $"Engineering {Guid.NewGuid():N}"
        });
        deptResponse.EnsureSuccessStatusCode();
        var dept = await deptResponse.Content.ReadFromJsonAsync<DepartmentPayload>();
        Assert.NotNull(dept);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId = dept!.Id,
            title = "Engineering Manager",
            isManagerial = true
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PositionProfilePayload>();
        Assert.NotNull(payload);
        Assert.Equal(dept.Id, payload!.DepartmentId);
        Assert.True(payload.IsManagerial);
    }

    [Fact]
    public async Task Post_PositionProfiles_Returns_Conflict_For_Duplicate_Title()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var first = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            title = "Recruiter",
            isManagerial = false
        });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            title = "Recruiter",
            isManagerial = false
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_PositionProfiles_Returns_NotFound_For_Unknown_Department()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId = Guid.NewGuid(),
            title = "Developer",
            isManagerial = false
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record DepartmentPayload(Guid Id);

    private sealed record PositionProfilePayload(
        Guid Id,
        Guid CompanyId,
        Guid? DepartmentId,
        string Title,
        string? Description,
        bool IsManagerial,
        bool IsActive,
        DateTimeOffset CreatedAt);
}
