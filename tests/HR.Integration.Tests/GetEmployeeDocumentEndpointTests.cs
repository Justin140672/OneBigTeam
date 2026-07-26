using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetEmployeeDocumentEndpointTests : IClassFixture<ApiWebApplicationFactory>
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
        using var client = AuthenticatedClient();
        var response     = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{SarahEmployeeId}/documents/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_When_EmployeeId_Does_Not_Match()
    {
        using var client = AuthenticatedClient();
        var response     = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{Guid.NewGuid()}/documents/{SarahContractDocId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_OK_With_Document_Details_For_Seeded_Document()
    {
        using var client = AuthenticatedClient();
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
        using var client = AdminClient();
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
        Assert.NotNull(                           payload.DownloadUrl);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private HttpClient AuthenticatedClient()
    {
        var userId = Guid.NewGuid();
        TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee).GetAwaiter().GetResult();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        return client;
    }

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
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
        DateOnly? ExpiryDate,
        string   DownloadUrl);
}
