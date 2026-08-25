using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// DOC-05: GET .../documents/{id}/versions is gated the same way as GetArchivedEmployeeDocuments
// (DOC-04) — "employee:manage" plus DocumentResourceAuthorizer.IsHrAdministratorAsync — an
// HR-only scope narrower than the self/manager-hierarchy CanAccessEmployeeDocumentsAsync check
// used by normal document read endpoints.
[Collection("Integration")]
public class GetEmployeeDocumentVersionHistoryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AcmeCompanyId      = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AcmeContractTypeId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid HrAdmin            = Guid.Parse("77000006-0000-0000-0000-000000000001");
    private static readonly Guid ManagerUser        = Guid.Parse("77000006-0000-0000-0000-000000000002");

    public GetEmployeeDocumentVersionHistoryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{Guid.NewGuid()}/documents/{Guid.NewGuid()}/versions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_NonHr_Employee_Or_Manager()
    {
        var employeeId   = Guid.NewGuid();
        using var hrClient = await HrAdminClient();
        var uploaded       = await Upload(hrClient, employeeId);

        using var managerClient = _factory.CreateClient();
        managerClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ManagerUser.ToString());
        managerClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, ManagerUser, SystemRoles.Employee, AcmeCompanyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, ManagerUser, SystemRoles.Manager, AcmeCompanyId);

        var response = await managerClient.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{uploaded.EmployeeDocumentId}/versions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Wrong_Company()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString()); // mismatched tenant

        var response = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{Guid.NewGuid()}/documents/{Guid.NewGuid()}/versions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_For_Unknown_Id()
    {
        using var client = await HrAdminClient();

        var response = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{Guid.NewGuid()}/documents/{Guid.NewGuid()}/versions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_OK_With_Full_History_For_HrAdministrator()
    {
        var employeeId   = Guid.NewGuid();
        using var client = await HrAdminClient();
        var original     = await Upload(client, employeeId, "Passport");

        var versionResponse = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{original.EmployeeDocumentId}/versions",
            BuildPdfUpload());
        versionResponse.EnsureSuccessStatusCode();
        var newVersion = await versionResponse.Content.ReadFromJsonAsync<UploadVersionPayload>();

        var response = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents/{original.EmployeeDocumentId}/versions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Versions.Count);
        Assert.Equal(newVersion!.EmployeeDocumentId, payload.Versions[0].EmployeeDocumentId); // newest first
        Assert.True(payload.Versions[0].IsLatestVersion);
        Assert.Equal(original.EmployeeDocumentId, payload.Versions[1].EmployeeDocumentId);
        Assert.False(payload.Versions[1].IsLatestVersion);
    }

    private async Task<UploadPayload> Upload(HttpClient client, Guid employeeId, string title = "Test Contract")
    {
        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/documents",
            BuildPdfUpload(title));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UploadPayload>())!;
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
    private sealed record UploadVersionPayload(Guid EmployeeDocumentId);
    private sealed record HistoryPayload(IReadOnlyList<HistoryItem> Versions);
    private sealed record HistoryItem(Guid EmployeeDocumentId, bool IsLatestVersion);
}
