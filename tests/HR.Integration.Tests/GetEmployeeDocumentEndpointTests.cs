using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetEmployeeDocumentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser       = Guid.Parse("11100010-0000-0000-0000-000000000001");
    private static readonly Guid AcmeCompanyId   = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AcmeContractId  = Guid.Parse("50000000-0000-0000-0000-000000000001");

    // Seeded: Sarah Chen's contract
    private static readonly Guid SarahEmployeeId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid SarahContractDocId = Guid.Parse("70000000-0000-0000-0000-000000000001");

    public GetEmployeeDocumentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{SarahEmployeeId}/documents/{SarahContractDocId}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_For_Unknown_Document()
    {
        // DOC-01: uses an HR-administrator caller, which is unconditionally in-scope for
        // SarahEmployeeId — a plain, unrelated employee caller is now denied with 403 before the
        // handler's NotFound lookup ever runs (see DocumentsResourceAuthorizationTests).
        using var client = await AdminClient();
        var response     = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{SarahEmployeeId}/documents/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_When_EmployeeId_Does_Not_Match()
    {
        // DOC-01: uses an HR-administrator caller so the mismatched-employeeId 404 (from the
        // handler) is what's under test here, not resource authorization (see
        // DocumentsResourceAuthorizationTests for the peer-denial case).
        using var client = await AdminClient();
        var response     = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{Guid.NewGuid()}/documents/{SarahContractDocId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_OK_With_Document_Details_For_Seeded_Document()
    {
        using var client = await AdminClient();
        var response     = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{SarahEmployeeId}/documents/{SarahContractDocId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DocPayload>();
        Assert.Equal(SarahContractDocId, payload!.EmployeeDocumentId);
        Assert.Equal(SarahEmployeeId,    payload.EmployeeId);
        Assert.Equal(AcmeCompanyId,      payload.CompanyId);
        Assert.NotEmpty(                 payload.Title);
        Assert.NotEmpty(                 payload.FileName);
    }

    [Fact]
    public async Task Upload_Then_Get_Returns_Uploaded_Document_Details()
    {
        using var client = await AdminClient();
        var employeeId   = Guid.NewGuid();

        // Upload a document
        var uploadResp = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents",
            BuildPdfUpload(AcmeContractId, "Get Test Doc", expiryDate: new DateOnly(2028, 1, 1)));
        uploadResp.EnsureSuccessStatusCode();
        var uploaded = await uploadResp.Content.ReadFromJsonAsync<UploadPayload>();

        // Fetch it by ID
        var getResp = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{uploaded!.EmployeeDocumentId}");

        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var payload = await getResp.Content.ReadFromJsonAsync<DocPayload>();
        Assert.Equal(uploaded.EmployeeDocumentId, payload!.EmployeeDocumentId);
        Assert.Equal(employeeId,                  payload.EmployeeId);
        Assert.Equal("Get Test Doc",              payload.Title);
        Assert.Equal(new DateOnly(2028, 1, 1),    payload.ExpiryDate);
    }

    [Fact]
    public async Task Response_Body_Does_Not_Contain_A_DownloadUrl_Property()
    {
        // DOC-02: the detail endpoint used to leak a signed download URL, bypassing virus-scan
        // gating and download auditing. Assert the raw JSON body has no such property at all,
        // rather than just relying on the strongly-typed DTO no longer declaring the field.
        using var client = await AdminClient();
        var response     = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{SarahEmployeeId}/documents/{SarahContractDocId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.False(doc.RootElement.TryGetProperty("downloadUrl", out _));
        Assert.False(doc.RootElement.TryGetProperty("uri", out _));
        Assert.False(doc.RootElement.TryGetProperty("url", out _));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Infected")]
    [InlineData("Failed")]
    public async Task Returns_OK_With_Metadata_Regardless_Of_Scan_Status(string scanStatusName)
    {
        // DOC-02: the detail endpoint is metadata-only and must never gate on scan status - only
        // the download endpoint does that (see DocumentScanStatusGatingEndpointTests).
        // FileScanStatus is internal, so [InlineData] uses a string and we parse it here rather
        // than exposing the enum on a public test method signature (CS0051).
        var scanStatus = Enum.Parse<FileScanStatus>(scanStatusName);
        var employeeId = Guid.NewGuid();
        var employeeDocumentId = await SeedDocumentWithScanStatusAsync(employeeId, scanStatus);

        using var client = await AdminClient();
        var response = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{employeeDocumentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DocPayload>();
        Assert.Equal(employeeDocumentId, payload!.EmployeeDocumentId);
    }

    private async Task<Guid> SeedDocumentWithScanStatusAsync(Guid employeeId, FileScanStatus scanStatus)
    {
        using var scope = _factory.Services.CreateScope();
        var db  = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        var now = DateTimeOffset.UtcNow;

        var docType = DocumentType.Create(Guid.NewGuid(), AcmeCompanyId, $"Detail Scan Type {Guid.NewGuid()}", null, now);
        db.DocumentTypes.Add(docType);

        var document = Document.Create(
            Guid.NewGuid(), AcmeCompanyId, employeeId, "Detail Scan Test", null,
            docType.Id, "detail-scan.pdf", 1024, "application/pdf",
            $"{AcmeCompanyId}/{employeeId}/detail-scan.pdf", null, AdminUser, now);

        switch (scanStatus)
        {
            case FileScanStatus.Pending:
                break;
            case FileScanStatus.Infected:
                document.MarkScanInfected("EICAR.Test.File", now);
                break;
            case FileScanStatus.Failed:
                document.MarkScanFailed("scanner unreachable", now);
                break;
        }
        db.Documents.Add(document);

        var employeeDocument = EmployeeDocument.Create(
            Guid.NewGuid(), AcmeCompanyId, employeeId, document.Id, AdminUser, now);
        db.EmployeeDocuments.Add(employeeDocument);

        await db.SaveChangesAsync();
        return employeeDocument.Id;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, AcmeCompanyId);
        return client;
    }

    private static MultipartFormDataContent BuildPdfUpload(
        Guid documentTypeId, string title = "Test Contract", DateOnly? expiryDate = null)
    {
        var pdfBytes = new byte[1024];
        pdfBytes[0] = 0x25; pdfBytes[1] = 0x50; pdfBytes[2] = 0x44; pdfBytes[3] = 0x46;

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(title),                  "Title");
        content.Add(new StringContent(documentTypeId.ToString()), "DocumentTypeId");

        if (expiryDate.HasValue)
            content.Add(new StringContent(expiryDate.Value.ToString("yyyy-MM-dd")), "ExpiryDate");

        var file = new ByteArrayContent(pdfBytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(file, "File", "test.pdf");

        return content;
    }

    private sealed record UploadPayload(Guid EmployeeDocumentId, Guid EmployeeId);
    private sealed record DocPayload(
        Guid     EmployeeDocumentId,
        Guid     EmployeeId,
        Guid     CompanyId,
        string   Title,
        string   FileName,
        string   ContentType,
        DateOnly? ExpiryDate);
}
