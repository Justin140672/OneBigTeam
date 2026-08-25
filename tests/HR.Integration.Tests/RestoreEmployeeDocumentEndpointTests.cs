using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class RestoreEmployeeDocumentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AcmeCompanyId      = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AcmeContractTypeId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid HrAdmin             = Guid.Parse("66000005-0000-0000-0000-000000000001");
    private static readonly Guid ManagerUser         = Guid.Parse("66000005-0000-0000-0000-000000000002");

    public RestoreEmployeeDocumentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{Guid.NewGuid()}/documents/{Guid.NewGuid()}/restore",
            EmptyJson());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_Without_HrAdministrator_Role()
    {
        var employeeId = Guid.NewGuid();
        using var hrClient = await HrAdminClient();
        var uploaded = await UploadAndDelete(hrClient, employeeId);

        using var managerClient = _factory.CreateClient();
        managerClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ManagerUser.ToString());
        managerClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, ManagerUser, SystemRoles.Employee, AcmeCompanyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, ManagerUser, SystemRoles.Manager, AcmeCompanyId);

        var response = await managerClient.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{uploaded.EmployeeDocumentId}/restore",
            EmptyJson());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_For_Unknown_Document()
    {
        using var client = await HrAdminClient();
        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{Guid.NewGuid()}/documents/{Guid.NewGuid()}/restore",
            EmptyJson());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Conflict_When_Document_Is_Not_Archived()
    {
        var employeeId = Guid.NewGuid();
        using var client = await HrAdminClient();

        var uploadResponse = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents",
            BuildPdfUpload());
        uploadResponse.EnsureSuccessStatusCode();
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<UploadPayload>();

        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{uploaded!.EmployeeDocumentId}/restore",
            EmptyJson());
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Returns_OK_And_Document_Reappears_In_Normal_List_After_Restore()
    {
        var employeeId = Guid.NewGuid();
        using var client = await HrAdminClient();
        var uploaded = await UploadAndDelete(client, employeeId);

        var restoreResponse = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{uploaded.EmployeeDocumentId}/restore",
            EmptyJson());
        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);

        var listResponse = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents");
        var payload = await listResponse.Content.ReadFromJsonAsync<DocsPayload>();
        Assert.Contains(payload!.Items, i => i.EmployeeDocumentId == uploaded.EmployeeDocumentId);

        var archivedListResponse = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/archived");
        var archivedPayload = await archivedListResponse.Content.ReadFromJsonAsync<DocsPayload>();
        Assert.DoesNotContain(archivedPayload!.Items, i => i.EmployeeDocumentId == uploaded.EmployeeDocumentId);
    }

    private async Task<UploadPayload> UploadAndDelete(HttpClient client, Guid employeeId)
    {
        var uploadResponse = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents",
            BuildPdfUpload());
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

    private static MultipartFormDataContent BuildPdfUpload(string title = "Restore Test Doc")
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
    private sealed record DocsPayload(IReadOnlyList<DocItem> Items);
    private sealed record DocItem(Guid EmployeeDocumentId, string Title);
}
