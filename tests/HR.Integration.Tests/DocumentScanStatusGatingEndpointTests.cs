using System.Net;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Covers ScanStatusAccessGuard end-to-end: every download/read endpoint that gates on a
/// scannable file's ScanStatus currently maps a validation failure to 404 NotFound (see each
/// endpoint's Configure()/HandleAsync — DownloadEmployeeDocument, DownloadSharedCompanyDocument,
/// DownloadSharedCompanyDocumentVersion, GetEmployeeProfilePhoto all `TypedResults.NotFound` on
/// any Result.Failure, not just "not found").
///
/// Unlike most other tests in this suite, Hangfire's in-process server IS actually running in
/// this test host (AddHangfireServer), so ScanUploadedFileJob really does execute after a real
/// upload — racing any state a test tries to force afterwards. To keep these tests deterministic,
/// rows are seeded directly via DocumentsDbContext (never through the real upload endpoint, so no
/// scan job is ever enqueued for them) rather than uploading and then trying to catch the row in
/// a Pending state before the background job gets to it.
/// </summary>
[Collection("Integration")]
public class DocumentScanStatusGatingEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AcmeCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Admin         = Guid.Parse("55555555-0000-0000-0000-000000000002");

    public DocumentScanStatusGatingEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, Admin, SystemRoles.HrAdministrator, AcmeCompanyId);
            await TestRoleSeeder.AssignRoleAsync(factory, Admin, SystemRoles.Employee, AcmeCompanyId);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task DownloadEmployeeDocument_Returns_NotFound_While_Document_Is_Pending()
    {
        var employeeId = Guid.NewGuid();
        var (documentId, employeeDocumentId) = await SeedEmployeeDocumentAsync(employeeId, FileScanStatus.Pending);

        using var noRedirect = NoRedirectManagerClient();
        var response = await noRedirect.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{employeeDocumentId}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadEmployeeDocument_Returns_NotFound_While_Document_Is_Infected()
    {
        var employeeId = Guid.NewGuid();
        var (documentId, employeeDocumentId) = await SeedEmployeeDocumentAsync(employeeId, FileScanStatus.Infected);

        using var noRedirect = NoRedirectManagerClient();
        var response = await noRedirect.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{employeeDocumentId}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadEmployeeDocument_Redirects_Once_Document_Is_Marked_Clean()
    {
        var employeeId = Guid.NewGuid();
        var (documentId, employeeDocumentId) = await SeedEmployeeDocumentAsync(employeeId, FileScanStatus.Clean);

        using var noRedirect = NoRedirectManagerClient();
        var response = await noRedirect.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{employeeDocumentId}/download");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }

    // Anonymous (401) coverage for this endpoint already exists in
    // DownloadEmployeeDocumentEndpointTests.Returns_Unauthorized_Without_Auth — not duplicated
    // here.

    private async Task<(Guid DocumentId, Guid EmployeeDocumentId)> SeedEmployeeDocumentAsync(
        Guid employeeId, FileScanStatus scanStatus)
    {
        using var scope = _factory.Services.CreateScope();
        var db  = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        var now = DateTimeOffset.UtcNow;

        var docType = DocumentType.Create(Guid.NewGuid(), AcmeCompanyId, $"Scan Gating Type {Guid.NewGuid()}", null, now);
        db.DocumentTypes.Add(docType);

        var document = Document.Create(
            Guid.NewGuid(), AcmeCompanyId, employeeId, "Scan Gating Test", null,
            docType.Id, "scan-gating.pdf", 1024, "application/pdf",
            $"{AcmeCompanyId}/{employeeId}/scan-gating.pdf", null, Admin, now);

        switch (scanStatus)
        {
            case FileScanStatus.Pending:
                break; // Create() already defaults to Pending.
            case FileScanStatus.Clean:
                document.MarkScanClean(now);
                break;
            case FileScanStatus.Infected:
                document.MarkScanInfected("EICAR.Test.File", now);
                break;
            case FileScanStatus.Scanning:
                document.MarkScanning(now);
                break;
            case FileScanStatus.Failed:
                document.MarkScanFailed("scanner unreachable", now);
                break;
        }
        db.Documents.Add(document);

        var employeeDocument = EmployeeDocument.Create(
            Guid.NewGuid(), AcmeCompanyId, employeeId, document.Id, Admin, now);
        db.EmployeeDocuments.Add(employeeDocument);

        await db.SaveChangesAsync();

        return (document.Id, employeeDocument.Id);
    }

    private HttpClient NoRedirectManagerClient()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Admin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        return client;
    }
}
