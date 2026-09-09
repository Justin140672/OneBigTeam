using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): Department.version coverage for
// PUT .../departments/{id}, against the real Postgres-backed ApiWebApplicationFactory.
// Follows UpdateDepartmentEndpointTests for seeding/auth helpers. The pre-edit Version is read
// from GET .../departments/{id}, which carries it.
[Collection("Integration")]
public class UpdateDepartmentConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeaa0002-0000-0000-0000-000000000001");

    public UpdateDepartmentConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.HrAdministrator);
            // GetDepartment (used to read the pre-edit Version) is gated on role:employee.
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_Department_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/departments/{Guid.NewGuid()}", new { name = "X" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var (client, companyId, id) = await SeedAsync();
        var version = (await GetAsync(client, companyId, id)).Version;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/departments/{id}", Body(companyId, id, "Editor A", version));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(version + 1, (await editorA.Content.ReadFromJsonAsync<DeptPayload>())!.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/departments/{id}", Body(companyId, id, "Editor B", version));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Equal("concurrency", (await editorB.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        var after = await GetAsync(client, companyId, id);
        Assert.Equal("Editor A", after.Name);
        Assert.Equal(version + 1, after.Version);
    }

    [Fact]
    public async Task Put_Department_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, id) = await SeedAsync();
        var version = (await GetAsync(client, companyId, id)).Version;

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/departments/{id}", Body(companyId, id, "Updated", version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<DeptPayload>())!;
        Assert.Equal(version + 1, payload.Version);
        Assert.Equal("Updated", payload.Name);
    }

    [Fact]
    public async Task Put_Department_Without_ExpectedVersion_Is_Rejected()
    {
        var (client, companyId, id) = await SeedAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/departments/{id}", Body(companyId, id, "NoVersion", expectedVersion: null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private static object Body(Guid companyId, Guid id, string name, int? expectedVersion)
        => new { companyId, id, name, expectedVersion };

    private async Task<(HttpClient Client, Guid CompanyId, Guid Id)> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.HrAdministrator, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments",
            new { companyId, name = $"Engineering {Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<DeptPayload>())!.Id;
        return (client, companyId, id);
    }

    private static async Task<DeptPayload> GetAsync(HttpClient client, Guid companyId, Guid id)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/departments/{id}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DeptPayload>())!;
    }

    private sealed record ErrorPayload(string? Error, string? Code);
    private sealed record DeptPayload(Guid Id, string Name, int Version);
}
