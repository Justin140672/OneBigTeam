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

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            title = "Test Engineer",
            isManagerial = false
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
        Assert.False(payload.IsManagerial);
        Assert.True(payload.IsActive);
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
    public async Task Get_PositionProfile_Returns_NotFound_When_Profile_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var client = AdminClient(companyA);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyA}/position-profiles", new
        {
            companyId = companyA,
            title = "Analyst",
            isManagerial = false
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<PositionProfilePayload>();
        Assert.NotNull(created);

        var response = await client.GetAsync($"/api/companies/{companyB}/position-profiles/{created!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record PositionProfilePayload(
        Guid Id,
        Guid CompanyId,
        Guid? DepartmentId,
        string Title,
        string? Description,
        bool IsManagerial,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
