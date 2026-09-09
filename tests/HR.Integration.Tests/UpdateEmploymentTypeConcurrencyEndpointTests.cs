using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): EmploymentType.version coverage for
// PUT .../employment-types/{id}. The pre-edit Version is read from the ListEmploymentTypes item.
[Collection("Integration")]
public class UpdateEmploymentTypeConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeaa0002-0000-0000-0000-000000000004");

    public UpdateEmploymentTypeConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_EmploymentType_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employment-types/{Guid.NewGuid()}", new { name = "X" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var (client, companyId, id) = await SeedAsync();
        var version = (await GetItemAsync(client, companyId, id)).Version;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employment-types/{id}", Body(companyId, id, "Editor A", version));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(version + 1, (await editorA.Content.ReadFromJsonAsync<EmploymentTypePayload>())!.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employment-types/{id}", Body(companyId, id, "Editor B", version));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Equal("concurrency", (await editorB.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        var after = await GetItemAsync(client, companyId, id);
        Assert.Equal("Editor A", after.Name);
        Assert.Equal(version + 1, after.Version);
    }

    [Fact]
    public async Task Put_EmploymentType_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, id) = await SeedAsync();
        var version = (await GetItemAsync(client, companyId, id)).Version;

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employment-types/{id}", Body(companyId, id, "Updated", version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<EmploymentTypePayload>())!;
        Assert.Equal(version + 1, payload.Version);
        Assert.Equal("Updated", payload.Name);
    }

    [Fact]
    public async Task Put_EmploymentType_Without_ExpectedVersion_Is_Rejected()
    {
        var (client, companyId, id) = await SeedAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employment-types/{id}", Body(companyId, id, "NoVersion", expectedVersion: null));

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

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employment-types",
            new { companyId, name = $"Permanent {Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<EmploymentTypePayload>())!.Id;
        return (client, companyId, id);
    }

    private static async Task<EmploymentTypePayload> GetItemAsync(HttpClient client, Guid companyId, Guid id)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/employment-types");
        response.EnsureSuccessStatusCode();
        var payload = (await response.Content.ReadFromJsonAsync<ListPayload>())!;
        return payload.Items.Single(i => i.Id == id);
    }

    private sealed record ErrorPayload(string? Error, string? Code);
    private sealed record ListPayload(IReadOnlyList<EmploymentTypePayload> Items);
    private sealed record EmploymentTypePayload(Guid Id, string Name, int Version);
}
