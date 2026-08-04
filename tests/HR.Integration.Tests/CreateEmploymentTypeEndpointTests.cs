using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateEmploymentTypeEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("aa000002-0000-0000-0000-000000000001");

    public CreateEmploymentTypeEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Post_EmploymentTypes_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{Guid.NewGuid()}/employment-types", new
        {
            name = "Permanent"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_EmploymentTypes_Creates_EmploymentType()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employment-types", new
        {
            companyId,
            name = "Permanent",
            description = "Full-time permanent employee"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<EmploymentTypePayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Permanent", payload.Name);
        Assert.Equal("Full-time permanent employee", payload.Description);
        Assert.True(payload.IsActive);
    }

    [Fact]
    public async Task Post_EmploymentTypes_Returns_Conflict_For_Duplicate_Name()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var first = await client.PostAsJsonAsync($"/api/companies/{companyId}/employment-types", new
        {
            companyId,
            name = "Contractor"
        });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync($"/api/companies/{companyId}/employment-types", new
        {
            companyId,
            name = "Contractor"
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    private sealed record EmploymentTypePayload(
        Guid Id,
        Guid CompanyId,
        string Name,
        string? Description,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
