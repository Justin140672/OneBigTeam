using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Covers POST /api/companies/{companyId}/shared-documents/{documentId}/reissue-acknowledgement
/// (ReissueSharedCompanyDocumentAcknowledgement): the shared-document:manage policy, tenant
/// isolation, and the handler's guard branches — document must exist, be Published, and require
/// acknowledgement. The "notify outstanding employees" happy path is exercised with an empty
/// audience so it returns a NotifiedCount of 0 without needing seeded Employee rows.
/// </summary>
[Collection("Integration")]
public class ReissueSharedCompanyDocumentAcknowledgementEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ReissueSharedCompanyDocumentAcknowledgementEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Reissue_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/api/companies/{Guid.NewGuid()}/shared-documents/{Guid.NewGuid()}/reissue-acknowledgement", EmptyJson());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reissue_Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager);
        using var client = await ClientAs(companyId, userId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{Guid.NewGuid()}/reissue-acknowledgement", EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reissue_Returns_Forbidden_When_Tenant_Does_Not_Match_Route()
    {
        var companyId        = Guid.NewGuid();
        var differentCompany = Guid.NewGuid();
        var userId           = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(differentCompany, userId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{Guid.NewGuid()}/reissue-acknowledgement", EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reissue_Returns_NotFound_For_Unknown_Document()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{Guid.NewGuid()}/reissue-acknowledgement", EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reissue_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA    = Guid.NewGuid();
        var hrInB    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        var docId = await UploadAsync(clientA, companyA, categoryInA);
        await PublishDirectlyAsync(companyA, docId, requiresAcknowledgement: true);

        using var clientB = await ClientAs(companyB, hrInB);
        var response = await clientB.PostAsync(
            $"/api/companies/{companyB}/shared-documents/{docId}/reissue-acknowledgement", EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reissue_Returns_UnprocessableEntity_For_A_Draft_Document()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var docId = await UploadAsync(client, companyId, categoryId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{docId}/reissue-acknowledgement", EmptyJson());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Reissue_Returns_UnprocessableEntity_When_Document_Does_Not_Require_Acknowledgement()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var docId = await UploadAsync(client, companyId, categoryId);
        await PublishDirectlyAsync(companyId, docId, requiresAcknowledgement: false);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{docId}/reissue-acknowledgement", EmptyJson());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Reissue_Succeeds_With_Zero_Notified_When_Audience_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var docId = await UploadAsync(client, companyId, categoryId);
        await PublishDirectlyAsync(companyId, docId, requiresAcknowledgement: true);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{docId}/reissue-acknowledgement", EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReissuePayload>();
        Assert.Equal(0, payload!.EmployeesNotifiedCount);
    }

    private static async Task<Guid> CreateCategoryAsync(HttpClient client, Guid companyId, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/document-categories", new { name });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CategoryPayload>();
        return payload!.Id;
    }

    private async Task<Guid> UploadAsync(HttpClient client, Guid companyId, Guid categoryId, string title = "Test Document")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(title), "Title" },
            { new StringContent(categoryId.ToString()), "CategoryId" },
        };
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 500];
        magic.CopyTo(bytes, 0);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        form.Add(fileContent, "File", "policy.pdf");

        var response = await client.PostAsync($"/api/companies/{companyId}/shared-documents", form);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<DocumentPayload>();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Documents.Persistence.DocumentsDbContext>();
        var doc = await db.SharedCompanyDocuments.SingleAsync(d => d.Id == payload!.Id);
        doc.MarkScanClean(DateTimeOffset.UtcNow);
        var version = await db.SharedCompanyDocumentVersions
            .Where(v => v.SharedCompanyDocumentId == payload!.Id)
            .OrderByDescending(v => v.VersionNumber)
            .FirstAsync();
        version.MarkScanClean(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        return payload!.Id;
    }

    private async Task PublishDirectlyAsync(Guid companyId, Guid documentId, bool requiresAcknowledgement)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Documents.Persistence.DocumentsDbContext>();
        var doc = await db.SharedCompanyDocuments.SingleAsync(d => d.Id == documentId && d.CompanyId == companyId);
        doc.Publish(Guid.NewGuid(), DateTimeOffset.UtcNow);
        if (requiresAcknowledgement)
        {
            doc.SetAcknowledgementSettings(
                true, DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)), null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        }
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> ClientAs(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private static StringContent EmptyJson() => new("{}", Encoding.UTF8, "application/json");

    private sealed record CategoryPayload(Guid Id, Guid CompanyId, string Name, bool IsActive);
    private sealed record DocumentPayload(Guid Id, string Title, string Status, int VersionNumber);
    private sealed record ReissuePayload(int EmployeesNotifiedCount);
}
