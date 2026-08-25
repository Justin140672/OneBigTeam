using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// DOC-05: a new version is created as a NEW EmployeeDocument row rather than mutating the one it
// replaces — see UploadEmployeeDocumentVersionHandler. Uses the same access gate as normal document
// upload (HR-administrator/manager via "role:employee" + DocumentResourceAuthorizer.
// CanAccessEmployeeDocumentsAsync), not the narrower HR-only scope used by version history reads.
[Collection("Integration")]
public class UploadEmployeeDocumentVersionEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AcmeCompanyId      = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AcmeContractTypeId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid VersionAdmin        = Guid.Parse("77000005-0000-0000-0000-000000000001");

    public UploadEmployeeDocumentVersionEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{Guid.NewGuid()}/documents/{Guid.NewGuid()}/versions",
            BuildPdfUpload());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_For_Unknown_EmployeeDocumentId()
    {
        var employeeId   = Guid.NewGuid();
        using var client = await AdminClient();

        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{Guid.NewGuid()}/versions",
            BuildPdfUpload());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Conflict_When_Uploading_Version_On_Already_Superseded_Document()
    {
        var employeeId   = Guid.NewGuid();
        using var client = await AdminClient();
        var original     = await Upload(client, employeeId);

        var firstVersionResponse = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{original.EmployeeDocumentId}/versions",
            BuildPdfUpload());
        firstVersionResponse.EnsureSuccessStatusCode();

        // Attempting a second version against the now-superseded original must conflict.
        var secondAttempt = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{original.EmployeeDocumentId}/versions",
            BuildPdfUpload());

        Assert.Equal(HttpStatusCode.Conflict, secondAttempt.StatusCode);
    }

    [Fact]
    public async Task Returns_Validation_Error_When_File_Missing()
    {
        var employeeId   = Guid.NewGuid();
        using var client = await AdminClient();
        var original     = await Upload(client, employeeId);

        var content = new MultipartFormDataContent(); // no File part

        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{original.EmployeeDocumentId}/versions",
            content);

        // A missing file passes FluentValidation's NotNull check trivially against the model
        // binder's placeholder and is instead caught by the handler's IFileUploadValidator check,
        // which the endpoint maps to 422 (UnprocessableEntity) — the same behaviour as the sibling
        // UploadEmployeeDocument endpoint's own validation-failure mapping.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Happy_Path_Creates_New_Version_And_Old_No_Longer_Shows_In_List()
    {
        var employeeId   = Guid.NewGuid();
        using var client = await AdminClient();
        var original     = await Upload(client, employeeId, "Original Title");

        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{original.EmployeeDocumentId}/versions",
            BuildPdfUpload());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UploadVersionPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(original.EmployeeDocumentId, payload!.EmployeeDocumentId);
        Assert.Equal(original.EmployeeDocumentId, payload.PreviousVersionId);
        Assert.Equal("Original Title", payload.Title); // title carried forward

        var listResponse = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listPayload = await listResponse.Content.ReadFromJsonAsync<DocsPayload>();

        Assert.Contains(listPayload!.Items, i => i.EmployeeDocumentId == payload.EmployeeDocumentId);
        Assert.DoesNotContain(listPayload.Items, i => i.EmployeeDocumentId == original.EmployeeDocumentId);
    }

    private async Task<UploadPayload> Upload(HttpClient client, Guid employeeId, string title = "Test Contract")
    {
        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents",
            BuildPdfUpload(title));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UploadPayload>())!;
    }

    private async Task<HttpClient> AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, VersionAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, VersionAdmin, SystemRoles.HrAdministrator, AcmeCompanyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, VersionAdmin, SystemRoles.Employee, AcmeCompanyId);
        return client;
    }

    private static MultipartFormDataContent BuildPdfUpload(string title = "Test Contract")
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

    private sealed record UploadPayload(Guid EmployeeDocumentId, Guid EmployeeId);
    private sealed record UploadVersionPayload(Guid EmployeeDocumentId, Guid PreviousVersionId, string Title);
    private sealed record DocsPayload(IReadOnlyList<DocItem> Items);
    private sealed record DocItem(Guid EmployeeDocumentId, string Title);
}
