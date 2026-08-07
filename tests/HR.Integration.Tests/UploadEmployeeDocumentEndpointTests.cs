using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UploadEmployeeDocumentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AcmeCompanyId      = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AcmeContractTypeId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid UploadAdmin        = Guid.Parse("55555555-0000-0000-0000-000000000001");

    public UploadEmployeeDocumentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, UploadAdmin, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, UploadAdmin, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{Guid.NewGuid()}/documents",
            BuildPdfUpload(AcmeContractTypeId));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_When_Company_Claim_Mismatches()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());

        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{Guid.NewGuid()}/documents",
            BuildPdfUpload(AcmeContractTypeId));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_When_Non_Manager_Uploads_To_Different_Employee()
    {
        var userId       = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, AcmeCompanyId);

        // uploadedBy (userId) != employeeId in route
        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{Guid.NewGuid()}/documents",
            BuildPdfUpload(AcmeContractTypeId));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Employee_Upload_Even_To_Own_Record()
    {
        // Employees must use the document-request upload endpoint; direct upload is manager-only.
        var userId       = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, AcmeCompanyId);

        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{userId}/documents",
            BuildPdfUpload(AcmeContractTypeId));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Created_When_Manager_Uploads_Valid_Document()
    {
        var employeeId   = Guid.NewGuid();
        using var client = await ManagerClient();

        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents",
            BuildPdfUpload(AcmeContractTypeId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UploadPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.EmployeeDocumentId);
        Assert.Equal(AcmeCompanyId, payload.CompanyId);
        Assert.Equal(employeeId, payload.EmployeeId);
        Assert.Equal("Test Contract", payload.Title);
        Assert.Equal("test.pdf", payload.FileName);
    }

    [Fact]
    public async Task Uploaded_Document_Appears_In_List()
    {
        var employeeId   = Guid.NewGuid();
        using var client = await ManagerClient();

        await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents",
            BuildPdfUpload(AcmeContractTypeId, "Listed Doc"));

        var listResponse = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var payload = await listResponse.Content.ReadFromJsonAsync<DocsPayload>();
        Assert.Single(payload!.Items);
        Assert.Equal("Listed Doc", payload.Items[0].Title);
    }

    [Fact]
    public async Task Uploaded_Document_Can_Be_Downloaded()
    {
        var employeeId   = Guid.NewGuid();
        using var client = await ManagerClient();

        var uploadResponse = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents",
            BuildPdfUpload(AcmeContractTypeId, "Download Test"));
        uploadResponse.EnsureSuccessStatusCode();
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<UploadPayload>();

        // Uploads are scanned asynchronously via a Hangfire job (ScanUploadedFileJob). Unlike
        // most other tests in this suite, Hangfire's in-process server IS actually running here
        // (AddHangfireServer), so the real job races with anything this test writes directly to
        // the DB — poll instead of doing a one-shot manual write, and fall back to forcing Clean
        // only if the job hasn't finished after a reasonable wait.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Documents.Persistence.DocumentsDbContext>();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                var current = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == uploaded!.DocumentId);
                if (current.ScanStatus == HR.Modules.Documents.Domain.FileScanStatus.Clean)
                    break;
                if (DateTime.UtcNow >= deadline)
                {
                    var doc = await db.Documents.SingleAsync(d => d.Id == uploaded!.DocumentId);
                    doc.MarkScanClean(DateTimeOffset.UtcNow);
                    await db.SaveChangesAsync();
                    break;
                }
                await Task.Delay(100);
            }
        }

        using var noRedirect = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        noRedirect.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UploadAdmin.ToString());
        noRedirect.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UploadAdmin, SystemRoles.HrAdministrator, AcmeCompanyId);

        var downloadResponse = await noRedirect.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{uploaded!.EmployeeDocumentId}/download");

        Assert.Equal(HttpStatusCode.Redirect, downloadResponse.StatusCode);
        Assert.NotNull(downloadResponse.Headers.Location);
    }

    private async Task<HttpClient> ManagerClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UploadAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UploadAdmin, SystemRoles.HrAdministrator, AcmeCompanyId);
        return client;
    }

    private static MultipartFormDataContent BuildPdfUpload(Guid documentTypeId, string title = "Test Contract")
    {
        var pdfBytes = new byte[1024];
        pdfBytes[0] = 0x25; pdfBytes[1] = 0x50; pdfBytes[2] = 0x44; pdfBytes[3] = 0x46; // %PDF

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(title), "Title");
        content.Add(new StringContent(documentTypeId.ToString()), "DocumentTypeId");

        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "File", "test.pdf");

        return content;
    }

    private sealed record UploadPayload(
        Guid DocumentId, Guid EmployeeDocumentId, Guid CompanyId, Guid EmployeeId,
        string Title, string FileName, long FileSize, string ContentType, Guid DocumentTypeId);

    private sealed record DocsPayload(IReadOnlyList<DocItem> Items);
    private sealed record DocItem(Guid EmployeeDocumentId, string Title, string DocumentTypeName);
}
