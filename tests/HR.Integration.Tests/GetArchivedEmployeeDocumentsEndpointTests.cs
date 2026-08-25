using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// DOC-04: GET .../documents/archived is deliberately gated by the narrower HR-administrator-only
// scope (DocumentResourceAuthorizer.IsHrAdministratorAsync), not the broader self/manager-hierarchy
// CanAccessEmployeeDocumentsAsync check used by the normal document endpoints — a direct manager
// who can view an employee's normal documents must NOT be able to view their archived documents.
[Collection("Integration")]
public class GetArchivedEmployeeDocumentsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AcmeCompanyId      = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AcmeContractTypeId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid HrAdmin             = Guid.Parse("66000004-0000-0000-0000-000000000001");
    private static readonly Guid ManagerUser         = Guid.Parse("66000004-0000-0000-0000-000000000002");

    public GetArchivedEmployeeDocumentsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{Guid.NewGuid()}/documents/archived");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_Without_HrAdministrator_Role()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());

        var response = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{Guid.NewGuid()}/documents/archived");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Manager_Who_Can_View_Normal_Documents()
    {
        // Proves the narrower HR-only scope: a manager (who is allowed through the normal
        // employee:manage/self-or-manager-hierarchy checks on the *non-archived* document
        // endpoints) must still be forbidden here.
        var employeeId = Guid.NewGuid();
        using var hrClient = await HrAdminClient();
        var upload = await UploadAndDelete(hrClient, employeeId);

        using var managerClient = _factory.CreateClient();
        managerClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ManagerUser.ToString());
        managerClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, ManagerUser, SystemRoles.Employee, AcmeCompanyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, ManagerUser, SystemRoles.Manager, AcmeCompanyId);

        var response = await managerClient.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/archived");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        _ = upload;
    }

    [Fact]
    public async Task Returns_OK_With_Archived_Items_For_HrAdministrator()
    {
        var employeeId = Guid.NewGuid();
        using var client = await HrAdminClient();
        var uploaded = await UploadAndDelete(client, employeeId);

        var response = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/archived");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ArchivedPayload>();
        Assert.Contains(payload!.Items, i => i.EmployeeDocumentId == uploaded.EmployeeDocumentId);
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

    private static MultipartFormDataContent BuildPdfUpload(string title = "Archived Test Doc")
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

    private sealed record UploadPayload(Guid EmployeeDocumentId);
    private sealed record ArchivedPayload(IReadOnlyList<ArchivedItem> Items);
    private sealed record ArchivedItem(Guid EmployeeDocumentId, string Title);
}
