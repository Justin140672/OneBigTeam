using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateEmploymentTypeEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("aa000003-0000-0000-0000-000000000001");

    public UpdateEmploymentTypeEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Put_EmploymentType_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/employment-types/{Guid.NewGuid()}", new
        {
            name = "Permanent"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_EmploymentType_Updates_Name_And_Description()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/employment-types", new
        {
            companyId,
            name = "Fixed Term"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<EmploymentTypePayload>();
        Assert.NotNull(created);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employment-types/{created!.Id}", new
            {
                companyId,
                id = created.Id,
                name = "Fixed-Term Contract",
                description = "Time-limited contract"
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = await updateResponse.Content.ReadFromJsonAsync<EmploymentTypePayload>();
        Assert.NotNull(updated);
        Assert.Equal("Fixed-Term Contract", updated!.Name);
        Assert.Equal("Time-limited contract", updated.Description);
    }

    [Fact]
    public async Task Put_EmploymentType_Returns_NotFound_For_Unknown_Id()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employment-types/{Guid.NewGuid()}", new
            {
                companyId,
                id = Guid.NewGuid(),
                name = "Permanent"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_EmploymentType_Returns_Conflict_For_Duplicate_Name()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var create1 = await client.PostAsJsonAsync($"/api/companies/{companyId}/employment-types", new
        {
            companyId, name = "Permanent"
        });
        create1.EnsureSuccessStatusCode();

        var create2 = await client.PostAsJsonAsync($"/api/companies/{companyId}/employment-types", new
        {
            companyId, name = "Contractor"
        });
        create2.EnsureSuccessStatusCode();
        var second = await create2.Content.ReadFromJsonAsync<EmploymentTypePayload>();
        Assert.NotNull(second);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employment-types/{second!.Id}", new
            {
                companyId,
                id = second.Id,
                name = "Permanent"
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed record EmploymentTypePayload(
        Guid Id,
        Guid CompanyId,
        string Name,
        string? Description,
        bool IsActive,
        DateTimeOffset UpdatedAt);
}
