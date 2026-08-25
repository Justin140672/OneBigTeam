using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class DeleteEmployeeDocumentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AcmeCompanyId      = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AcmeContractTypeId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid DeleteAdmin        = Guid.Parse("66666666-0000-0000-0000-000000000001");

    public DeleteEmployeeDocumentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, DeleteAdmin, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, DeleteAdmin, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.DeleteAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{Guid.NewGuid()}/documents/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_Without_Employee_Manage_Role()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());

        var response = await client.DeleteAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{Guid.NewGuid()}/documents/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_For_Unknown_Document()
    {
        using var client = await ManagerClient();
        var response     = await client.DeleteAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{Guid.NewGuid()}/documents/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NoContent_And_Document_Removed_From_List()
    {
        var employeeId   = Guid.NewGuid();
        using var client = await ManagerClient();

        var uploadResponse = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents",
            BuildPdfUpload(AcmeContractTypeId));
        uploadResponse.EnsureSuccessStatusCode();

        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<UploadPayload>();

        var deleteResponse = await client.DeleteAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{uploaded!.EmployeeDocumentId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents");
        var payload      = await listResponse.Content.ReadFromJsonAsync<DocsPayload>();
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Deleted_Document_Is_Retrievable_Via_Archived_List_Endpoint()
    {
        // DOC-04: closes the loop between delete (archive) and the new archived-list view — a
        // deleted document must not simply vanish, it must reappear here for HR to review/restore.
        var employeeId   = Guid.NewGuid();
        using var client = await ManagerClient();

        var uploadResponse = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents",
            BuildPdfUpload(AcmeContractTypeId));
        uploadResponse.EnsureSuccessStatusCode();
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<UploadPayload>();

        var deleteResponse = await client.DeleteAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{uploaded!.EmployeeDocumentId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var archivedResponse = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/archived");
        Assert.Equal(HttpStatusCode.OK, archivedResponse.StatusCode);
        var archivedPayload = await archivedResponse.Content.ReadFromJsonAsync<DocsPayload>();
        Assert.Contains(archivedPayload!.Items, i => i.EmployeeDocumentId == uploaded.EmployeeDocumentId);
    }

    private async Task<HttpClient> ManagerClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, DeleteAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, DeleteAdmin, SystemRoles.HrAdministrator, AcmeCompanyId);
        return client;
    }

    private static MultipartFormDataContent BuildPdfUpload(Guid documentTypeId, string title = "Test Document")
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

    private sealed record UploadPayload(Guid EmployeeDocumentId);
    private sealed record DocsPayload(IReadOnlyList<DocItem> Items);
    private sealed record DocItem(Guid EmployeeDocumentId, string Title);
}
