using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): CompanyDocumentCategory.version coverage for
// PUT .../document-categories/{categoryId}, against the real Postgres-backed
// ApiWebApplicationFactory. A freshly created category is known to be version 1; the post-edit
// version is read back from the update response (which carries Version) and from the list endpoint.
// The endpoint uses ProblemResults.FromError, so a stale save maps to 409 Conflict.
[Collection("Integration")]
public class UpdateCompanyDocumentCategoryConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("dddd0002-0000-0000-0000-000000000001");

    public UpdateCompanyDocumentCategoryConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_Category_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/document-categories/{Guid.NewGuid()}", new { name = "Renamed" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Category_Returns_NotFound_For_Unknown_Id()
    {
        var (client, companyId, _) = await CreateCategoryAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-categories/{Guid.NewGuid()}",
            Body(companyId, Guid.NewGuid(), name: "Renamed", expectedVersion: 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Category_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, id) = await CreateCategoryAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-categories/{id}", Body(companyId, id, name: "Company Policies", expectedVersion: 1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<CategoryPayload>())!;
        Assert.Equal(2, payload.Version);
        Assert.Equal("Company Policies", payload.Name);
    }

    [Fact]
    public async Task Put_Category_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var (client, companyId, id) = await CreateCategoryAsync();

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-categories/{id}", Body(companyId, id, name: "Rename One", expectedVersion: null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var list = await client.GetFromJsonAsync<ListPayload>($"/api/companies/{companyId}/document-categories");
        var current = Assert.Single(list!.Items, c => c.Id == id);
        Assert.Equal("Policy", current.Name);
        Assert.Equal(1, current.Version);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var (client, companyId, id) = await CreateCategoryAsync();
        const int initialVersion = 1;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-categories/{id}", Body(companyId, id, name: "EditorA", expectedVersion: initialVersion));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(initialVersion + 1, (await editorA.Content.ReadFromJsonAsync<CategoryPayload>())!.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-categories/{id}", Body(companyId, id, name: "EditorB", expectedVersion: initialVersion));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Equal("concurrency", (await editorB.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        var list = await client.GetFromJsonAsync<ListPayload>($"/api/companies/{companyId}/document-categories");
        var current = Assert.Single(list!.Items, c => c.Id == id);
        Assert.Equal("EditorA", current.Name);
        Assert.Equal(initialVersion + 1, current.Version);
    }

    private static object Body(Guid companyId, Guid categoryId, string name, int? expectedVersion)
        => new { companyId, categoryId, name, expectedVersion };

    private async Task<(HttpClient Client, Guid CompanyId, Guid Id)> CreateCategoryAsync()
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/document-categories", new { name = "Policy" });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CategoryPayload>())!;
        return (client, companyId, created.Id);
    }

    private sealed record ErrorPayload(string? Error, string? Code);
    private sealed record CategoryPayload(Guid Id, Guid CompanyId, string Name, bool IsActive, int Version);
    private sealed record ListPayload(IReadOnlyList<CategoryPayload> Items);
}
