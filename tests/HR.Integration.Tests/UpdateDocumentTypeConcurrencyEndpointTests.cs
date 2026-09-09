using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): DocumentType.version coverage for
// PUT .../document-types/{documentTypeId}, against the real Postgres-backed ApiWebApplicationFactory.
// A freshly created document type is known to be version 1; the post-edit version is read from the
// update response and the list endpoint (both carry Version). The endpoint uses
// ProblemResults.FromError, so a stale save maps to 409 Conflict.
[Collection("Integration")]
public class UpdateDocumentTypeConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("dddd0003-0000-0000-0000-000000000001");

    public UpdateDocumentTypeConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_DocumentType_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/document-types/{Guid.NewGuid()}", new { name = "Updated" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_DocumentType_Returns_NotFound_For_Unknown_Id()
    {
        var (client, companyId, _) = await CreateDocumentTypeAsync();
        var unknownId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-types/{unknownId}",
            Body(companyId, unknownId, name: "Ghost", expectedVersion: 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_DocumentType_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, id) = await CreateDocumentTypeAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-types/{id}", Body(companyId, id, name: "Updated Name", expectedVersion: 1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<DocTypePayload>())!;
        Assert.Equal(2, payload.Version);
        Assert.Equal("Updated Name", payload.Name);
    }

    [Fact]
    public async Task Put_DocumentType_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var (client, companyId, id) = await CreateDocumentTypeAsync();

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-types/{id}", Body(companyId, id, name: "Rename One", expectedVersion: null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var list = await client.GetFromJsonAsync<ListPayload>($"/api/companies/{companyId}/document-types");
        var current = Assert.Single(list!.Items, t => t.Id == id);
        Assert.Equal("Original Name", current.Name);
        Assert.Equal(1, current.Version);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var (client, companyId, id) = await CreateDocumentTypeAsync();
        const int initialVersion = 1;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-types/{id}", Body(companyId, id, name: "EditorA", expectedVersion: initialVersion));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(initialVersion + 1, (await editorA.Content.ReadFromJsonAsync<DocTypePayload>())!.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-types/{id}", Body(companyId, id, name: "EditorB", expectedVersion: initialVersion));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Equal("concurrency", (await editorB.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        var list = await client.GetFromJsonAsync<ListPayload>($"/api/companies/{companyId}/document-types");
        var current = Assert.Single(list!.Items, t => t.Id == id);
        Assert.Equal("EditorA", current.Name);
        Assert.Equal(initialVersion + 1, current.Version);
    }

    private static object Body(Guid companyId, Guid documentTypeId, string name, int? expectedVersion)
        => new { companyId, documentTypeId, name, allowEmployeeUpload = false, expectedVersion };

    private async Task<(HttpClient Client, Guid CompanyId, Guid Id)> CreateDocumentTypeAsync()
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-types", new { name = "Original Name", allowEmployeeUpload = false });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<DocTypePayload>())!;
        return (client, companyId, created.Id);
    }

    private sealed record ErrorPayload(string? Error, string? Code);
    private sealed record DocTypePayload(Guid Id, string Name, bool AllowEmployeeUpload, int Version);
    private sealed record ListPayload(IReadOnlyList<DocTypePayload> Items);
}
