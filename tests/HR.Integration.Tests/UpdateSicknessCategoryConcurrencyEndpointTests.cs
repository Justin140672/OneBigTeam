using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): SicknessCategory.version coverage for
// PUT .../sickness-categories/{id}, against the real Postgres-backed ApiWebApplicationFactory.
// There is no GET/List field carrying the version for this aggregate, so the pre-edit version of a
// freshly created category is known to be 1 and the post-edit version is read back from the update
// response (UpdateSicknessCategoryResponse carries Version).
[Collection("Integration")]
public class UpdateSicknessCategoryConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cccc0003-0000-0000-0000-000000000001");

    public UpdateSicknessCategoryConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_SicknessCategory_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/sickness-categories/{Guid.NewGuid()}", new { name = "Flu" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var (client, companyId, id) = await CreateCategoryAsync();
        const int initialVersion = 1;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/sickness-categories/{id}", Body(companyId, id, order: 5, expectedVersion: initialVersion));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        var editorAPayload = (await editorA.Content.ReadFromJsonAsync<CategoryPayload>())!;
        Assert.Equal(initialVersion + 1, editorAPayload.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/sickness-categories/{id}", Body(companyId, id, order: 9, expectedVersion: initialVersion));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Equal("concurrency", (await editorB.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        // A follow-up edit with the current version shows editor A's DisplayOrder, not editor B's.
        var reload = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/sickness-categories/{id}", Body(companyId, id, order: 5, expectedVersion: editorAPayload.Version));
        Assert.Equal(HttpStatusCode.OK, reload.StatusCode);
    }

    [Fact]
    public async Task Put_SicknessCategory_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, id) = await CreateCategoryAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/sickness-categories/{id}", Body(companyId, id, order: 3, expectedVersion: 1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<CategoryPayload>())!;
        Assert.Equal(2, payload.Version);
        Assert.Equal(3, payload.DisplayOrder);
    }

    [Fact]
    public async Task Put_SicknessCategory_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var (client, companyId, id) = await CreateCategoryAsync();

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/sickness-categories/{id}", Body(companyId, id, order: 2, expectedVersion: null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        // A follow-up edit with the known-good version 1 still succeeds -> the 422 wrote nothing.
        var reload = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/sickness-categories/{id}", Body(companyId, id, order: 4, expectedVersion: 1));
        Assert.Equal(HttpStatusCode.OK, reload.StatusCode);
        Assert.Equal(2, (await reload.Content.ReadFromJsonAsync<CategoryPayload>())!.Version);
    }

    private static object Body(Guid companyId, Guid id, int order, int? expectedVersion)
        => new { companyId, id, name = "Flu", displayOrder = order, expectedVersion };

    private async Task<(HttpClient Client, Guid CompanyId, Guid Id)> CreateCategoryAsync()
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories",
            new { companyId, name = "Flu", displayOrder = 1 });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CategoryPayload>())!;
        return (client, companyId, created.Id);
    }

    private sealed record ErrorPayload(string? Error, string? Code);
    private sealed record CategoryPayload(Guid Id, string Name, bool IsActive, int DisplayOrder, int Version);
}
