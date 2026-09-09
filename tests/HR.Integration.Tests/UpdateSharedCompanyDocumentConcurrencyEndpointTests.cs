using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): SharedCompanyDocument.version coverage for the three
// slices that all mutate the same aggregate row, against the real Postgres-backed
// ApiWebApplicationFactory:
//   - PUT .../shared-documents/{id}                          (metadata)
//   - PUT .../shared-documents/{id}/audience                 (audience)
//   - PUT .../shared-documents/{id}/acknowledgement-settings (acknowledgement settings)
// Each endpoint keeps its existing 404 (not_found) + 422 (validation / other) mapping and adds an
// explicit `concurrency => 409 Conflict` branch. The pre-edit Version is read from GET
// .../shared-documents/{id}, whose response carries it as the last field.
[Collection("Integration")]
public class UpdateSharedCompanyDocumentConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public UpdateSharedCompanyDocumentConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Metadata ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PutMetadata_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/shared-documents/{Guid.NewGuid()}",
            new { title = "T", categoryId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutMetadata_Returns_NotFound_For_Unknown_Document()
    {
        var (client, companyId, categoryId, _) = await SeedAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{Guid.NewGuid()}",
            new { title = "T", categoryId, expectedVersion = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutMetadata_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, categoryId, docId) = await SeedAsync();
        var version = (await GetAsync(client, companyId, docId)).Version;

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{docId}",
            new { title = "Updated Title", categoryId, expectedVersion = version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(version + 1, (await response.Content.ReadFromJsonAsync<VersionPayload>())!.Version);
    }

    [Fact]
    public async Task PutMetadata_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var (client, companyId, categoryId, docId) = await SeedAsync();

        var before = (await GetAsync(client, companyId, docId)).Version;

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{docId}", new { title = "One", categoryId });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        Assert.Equal(before, (await GetAsync(client, companyId, docId)).Version);
    }

    [Fact]
    public async Task PutMetadata_Two_Editors_Second_Stale_Save_Returns_409_Concurrency()
    {
        var (client, companyId, categoryId, docId) = await SeedAsync();
        var version = (await GetAsync(client, companyId, docId)).Version;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{docId}",
            new { title = "EditorA", categoryId, expectedVersion = version });
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(version + 1, (await editorA.Content.ReadFromJsonAsync<VersionPayload>())!.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{docId}",
            new { title = "EditorB", categoryId, expectedVersion = version });

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);

        Assert.Equal(version + 1, (await GetAsync(client, companyId, docId)).Version);
    }

    // ── Audience ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PutAudience_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/shared-documents/{Guid.NewGuid()}/audience", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutAudience_Returns_NotFound_For_Unknown_Document()
    {
        var (client, companyId, _, _) = await SeedAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{Guid.NewGuid()}/audience",
            new { expectedVersion = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutAudience_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, _, docId) = await SeedAsync();
        var deptId = await SeedDepartmentAsync(companyId, "Engineering");
        var version = (await GetAsync(client, companyId, docId)).Version;

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{docId}/audience",
            new { audienceDepartmentIds = new[] { deptId }, expectedVersion = version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(version + 1, (await response.Content.ReadFromJsonAsync<VersionPayload>())!.Version);
    }

    [Fact]
    public async Task PutAudience_Two_Editors_Second_Stale_Save_Returns_409_Concurrency()
    {
        var (client, companyId, _, docId) = await SeedAsync();
        var deptA = await SeedDepartmentAsync(companyId, "Engineering");
        var deptB = await SeedDepartmentAsync(companyId, "Finance");
        var version = (await GetAsync(client, companyId, docId)).Version;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{docId}/audience",
            new { audienceDepartmentIds = new[] { deptA }, expectedVersion = version });
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{docId}/audience",
            new { audienceDepartmentIds = new[] { deptB }, expectedVersion = version });

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Equal(version + 1, (await GetAsync(client, companyId, docId)).Version);
    }

    // ── Acknowledgement settings ────────────────────────────────────────────────

    [Fact]
    public async Task PutAcknowledgementSettings_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/shared-documents/{Guid.NewGuid()}/acknowledgement-settings",
            new { requiresAcknowledgement = false });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutAcknowledgementSettings_Returns_NotFound_For_Unknown_Document()
    {
        var (client, companyId, _, _) = await SeedAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{Guid.NewGuid()}/acknowledgement-settings",
            new { requiresAcknowledgement = false, expectedVersion = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutAcknowledgementSettings_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, _, docId) = await SeedAsync();
        var version = (await GetAsync(client, companyId, docId)).Version;

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{docId}/acknowledgement-settings",
            new { requiresAcknowledgement = true, acknowledgementDueDate = "2027-01-01", expectedVersion = version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(version + 1, (await response.Content.ReadFromJsonAsync<VersionPayload>())!.Version);
    }

    [Fact]
    public async Task PutAcknowledgementSettings_Two_Editors_Second_Stale_Save_Returns_409_Concurrency()
    {
        var (client, companyId, _, docId) = await SeedAsync();
        var version = (await GetAsync(client, companyId, docId)).Version;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{docId}/acknowledgement-settings",
            new { requiresAcknowledgement = true, acknowledgementDueDate = "2027-01-01", expectedVersion = version });
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{docId}/acknowledgement-settings",
            new { requiresAcknowledgement = true, acknowledgementDueDate = "2027-06-01", expectedVersion = version });

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Equal(version + 1, (await GetAsync(client, companyId, docId)).Version);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<(HttpClient Client, Guid CompanyId, Guid CategoryId, Guid DocumentId)> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var categoryResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-categories", new { name = "Policy" });
        categoryResponse.EnsureSuccessStatusCode();
        var categoryId = (await categoryResponse.Content.ReadFromJsonAsync<CategoryPayload>())!.Id;

        var form = new MultipartFormDataContent
        {
            { new StringContent("Test Document"), "Title" },
            { new StringContent(categoryId.ToString()), "CategoryId" },
        };
        var file = new ByteArrayContent(PdfBytes());
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        form.Add(file, "File", "policy.pdf");

        var uploadResponse = await client.PostAsync($"/api/companies/{companyId}/shared-documents", form);
        uploadResponse.EnsureSuccessStatusCode();
        var docId = (await uploadResponse.Content.ReadFromJsonAsync<DocumentPayload>())!.Id;

        // Uploads are scanned asynchronously via a Hangfire job that never runs in-test; simulate a
        // completed Clean scan so downstream reads work, matching SharedCompanyDocumentEndpointTests.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Documents.Persistence.DocumentsDbContext>();
        var doc = await db.SharedCompanyDocuments.SingleAsync(d => d.Id == docId);
        doc.MarkScanClean(DateTimeOffset.UtcNow);
        var version = await db.SharedCompanyDocumentVersions
            .Where(v => v.SharedCompanyDocumentId == docId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstAsync();
        version.MarkScanClean(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        return (client, companyId, categoryId, docId);
    }

    private async Task<Guid> SeedDepartmentAsync(Guid companyId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Employees.Persistence.EmployeesDbContext>();
        var department = HR.Modules.Employees.Domain.Department.Create(
            Guid.NewGuid(), companyId, name, null, DateTimeOffset.UtcNow);
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        return department.Id;
    }

    private static async Task<VersionPayload> GetAsync(HttpClient client, Guid companyId, Guid docId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/shared-documents/{docId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VersionPayload>())!;
    }

    private static byte[] PdfBytes()
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 500];
        magic.CopyTo(bytes, 0);
        return bytes;
    }

    private sealed record CategoryPayload(Guid Id);
    private sealed record DocumentPayload(Guid Id);
    private sealed record VersionPayload(int Version);
}
