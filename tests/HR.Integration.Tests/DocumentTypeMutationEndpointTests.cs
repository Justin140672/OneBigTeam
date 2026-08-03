using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class DocumentTypeMutationEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = Guid.Parse("11100009-0000-0000-0000-000000000001");

    public DocumentTypeMutationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    // ── UpdateDocumentType ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateDocumentType_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var companyId    = Guid.NewGuid();
        var response     = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-types/{Guid.NewGuid()}",
            new { name = "Updated" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDocumentType_Returns_Forbidden_Without_Employee_Manage_Role()
    {
        using var client = _factory.CreateClient();
        var companyId    = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-types/{Guid.NewGuid()}",
            new { name = "Updated" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDocumentType_Returns_NotFound_For_Unknown_Id()
    {
        var companyId    = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var unknownId    = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-types/{unknownId}",
            new { companyId, documentTypeId = unknownId, name = "Ghost", allowEmployeeUpload = false });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDocumentType_Returns_OK_And_Persists_Changes()
    {
        var companyId    = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var createResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-types",
            new { name = "Original Name", allowEmployeeUpload = false });
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<DocTypePayload>();

        var updateResp = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-types/{created!.Id}",
            new
            {
                companyId,
                documentTypeId     = created.Id,
                name               = "Updated Name",
                description        = "A description",
                allowEmployeeUpload = true
            });

        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updated = await updateResp.Content.ReadFromJsonAsync<DocTypePayload>();
        Assert.Equal("Updated Name", updated!.Name);
        Assert.True(                 updated.AllowEmployeeUpload);
    }

    [Fact]
    public async Task UpdateDocumentType_Returns_Conflict_When_Name_Already_Taken_In_Company()
    {
        var companyId    = Guid.NewGuid();
        using var client = AdminClient(companyId);

        // Create two types in the same isolated company
        var resp1 = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-types",
            new { name = "Type Alpha", allowEmployeeUpload = false });
        resp1.EnsureSuccessStatusCode();

        var resp2 = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-types",
            new { name = "Type Beta", allowEmployeeUpload = false });
        resp2.EnsureSuccessStatusCode();
        var beta = await resp2.Content.ReadFromJsonAsync<DocTypePayload>();

        // Try to rename Beta to Alpha
        var conflictResp = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-types/{beta!.Id}",
            new { companyId, documentTypeId = beta.Id, name = "Type Alpha", allowEmployeeUpload = false });

        Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode);
    }

    // ── DeactivateDocumentType ───────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateDocumentType_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var companyId    = Guid.NewGuid();
        var response     = await client.DeleteAsync(
            $"/api/companies/{companyId}/document-types/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateDocumentType_Returns_Forbidden_Without_Employee_Manage_Role()
    {
        using var client = _factory.CreateClient();
        var companyId    = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/document-types/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateDocumentType_Returns_NotFound_For_Unknown_Id()
    {
        var companyId    = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/document-types/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateDocumentType_Returns_NoContent_And_Removes_From_List()
    {
        var companyId    = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var createResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-types",
            new { name = "To Deactivate", allowEmployeeUpload = false });
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<DocTypePayload>();

        var deleteResp = await client.DeleteAsync(
            $"/api/companies/{companyId}/document-types/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        // Deactivated types should not appear in the active list
        var listResp = await client.GetAsync(
            $"/api/companies/{companyId}/document-types");
        var listPayload = await listResp.Content.ReadFromJsonAsync<DocTypeListPayload>();
        Assert.DoesNotContain(listPayload!.Items, dt => dt.Id == created.Id);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private sealed record DocTypePayload(Guid Id, string Name, bool AllowEmployeeUpload);
    private sealed record DocTypeListPayload(IReadOnlyList<DocTypeItem> Items);
    private sealed record DocTypeItem(Guid Id, string Name, bool IsActive);
}
