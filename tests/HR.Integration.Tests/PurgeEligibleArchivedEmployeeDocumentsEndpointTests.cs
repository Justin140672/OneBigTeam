using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Documents.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

// DOC-04: purge-eligible is gated by "role:company-administrator" — deliberately stronger/
// different from the "employee:manage" (HrAdministrator) policy used everywhere else in this
// module. A plain HrAdministrator without the CompanyAdministrator role must be forbidden.
[Collection("Integration")]
public class PurgeEligibleArchivedEmployeeDocumentsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AcmeCompanyId      = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AcmeContractTypeId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid HrAdmin             = Guid.Parse("66000006-0000-0000-0000-000000000001");
    private static readonly Guid CompanyAdmin        = Guid.Parse("66000006-0000-0000-0000-000000000002");

    public PurgeEligibleArchivedEmployeeDocumentsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/documents/archived/purge-eligible", EmptyJson());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_HrAdministrator_Without_CompanyAdministrator_Role()
    {
        using var client = await HrAdminClient();
        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/documents/archived/purge-eligible", EmptyJson());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_OK_For_CompanyAdministrator_And_Purges_Only_Old_Enough_Archives()
    {
        var employeeId = Guid.NewGuid();
        using var hrClient = await HrAdminClient();

        var eligible   = await UploadAndDelete(hrClient, employeeId, "Old Archived Doc");
        var tooRecent  = await UploadAndDelete(hrClient, employeeId, "Recently Archived Doc");

        // Backdate the "eligible" document's ArchivedAt beyond the 90-day retention window
        // directly via the DbContext — no HTTP surface exists to set this, mirroring
        // DocumentScanStatusGatingEndpointTests's pattern of seeding state below the API.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
            var ed = await db.EmployeeDocuments.SingleAsync(x => x.Id == eligible.EmployeeDocumentId);
            ed.Archive(HrAdmin, "backdated for purge test", DateTimeOffset.UtcNow.AddDays(-95));
            await db.SaveChangesAsync();
        }

        using var adminClient = await CompanyAdminClient();
        var response = await adminClient.PostAsync(
            $"/api/companies/{AcmeCompanyId}/documents/archived/purge-eligible", EmptyJson());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PurgePayload>();
        Assert.True(payload!.PurgedCount >= 1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
            Assert.False(await db.EmployeeDocuments.AnyAsync(x => x.Id == eligible.EmployeeDocumentId));
            Assert.True(await db.EmployeeDocuments.AnyAsync(x => x.Id == tooRecent.EmployeeDocumentId));
        }
    }

    private async Task<UploadPayload> UploadAndDelete(HttpClient client, Guid employeeId, string title)
    {
        var uploadResponse = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents",
            BuildPdfUpload(title));
        uploadResponse.EnsureSuccessStatusCode();
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<UploadPayload>();

        var deleteResponse = await client.DeleteAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{uploaded!.EmployeeDocumentId}");
        deleteResponse.EnsureSuccessStatusCode();

        return uploaded;
    }

    private async Task<HttpClient> HrAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, HrAdmin, SystemRoles.Employee, AcmeCompanyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, HrAdmin, SystemRoles.HrAdministrator, AcmeCompanyId);
        return client;
    }

    private async Task<HttpClient> CompanyAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, CompanyAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, CompanyAdmin, SystemRoles.Employee, AcmeCompanyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, CompanyAdmin, SystemRoles.CompanyAdministrator, AcmeCompanyId);
        return client;
    }

    private static MultipartFormDataContent BuildPdfUpload(string title)
    {
        var pdfBytes = new byte[1024];
        pdfBytes[0] = 0x25; pdfBytes[1] = 0x50; pdfBytes[2] = 0x44; pdfBytes[3] = 0x46; // %PDF

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(title), "Title");
        content.Add(new StringContent(AcmeContractTypeId.ToString()), "DocumentTypeId");

        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "File", "test.pdf");

        return content;
    }

    // FastEndpoints rejects with 415 Unsupported Media Type once past authorization — an empty
    // JSON body is the minimal content that satisfies model binding for this no-payload action.
    private static StringContent EmptyJson() =>
        new("{}", Encoding.UTF8, "application/json");

    private sealed record UploadPayload(Guid EmployeeDocumentId);
    private sealed record PurgePayload(int PurgedCount);
}
