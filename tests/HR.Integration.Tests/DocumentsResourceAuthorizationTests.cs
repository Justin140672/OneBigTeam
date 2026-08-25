using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Documents.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// DOC-01: resource-level (self / manager-hierarchy / HR-admin) authorization for the four
/// employee-document endpoints guarded by
/// <c>HR.Modules.Documents.Services.DocumentResourceAuthorizer</c>. Endpoint-level Policies(...)
/// only proves tenant/role membership; it never proves the caller has a relationship to the
/// specific employeeId in the route, so these tests exercise that resource-ownership check
/// end-to-end over real HTTP, mirroring LeaveResourceAuthorizationTests's pattern for LEAVE-01.
/// </summary>
[Collection("Integration")]
public class DocumentsResourceAuthorizationTests(ApiWebApplicationFactory factory)
{
    // Pre-seeded company/reference data reused by other Documents endpoint tests (see
    // ApiWebApplicationFactory seeding) — reused here to avoid re-seeding per test.
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AcmeContractTypeId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid EmploymentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid DepartmentId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LocationId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid PositionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002");

    private static readonly Guid OtherCompanyId = Guid.NewGuid();

    // ─────────────────────────────────────────────────────────────────────────
    // List
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/documents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_Allows_Employee_Viewing_Own_Documents()
    {
        var employee = await CreateEmployeeAsync();
        using var client = await AuthenticatedClient(employee);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task List_Returns_Forbidden_For_Unrelated_Peer_Employee()
    {
        var employee = await CreateEmployeeAsync();
        var peer = await CreateEmployeeAsync();
        using var client = await AuthenticatedClient(peer);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_Allows_Direct_Manager()
    {
        var manager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        using var client = await AuthenticatedClient(manager);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{report}/documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task List_Allows_Skip_Level_Manager_In_Three_Level_Hierarchy()
    {
        var seniorManager = await CreateEmployeeAsync(); // C
        var manager = await CreateEmployeeAsync();       // B
        var employee = await CreateEmployeeAsync();      // A

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, employee, manager);
            await AssignManagerAsync(setupClient, manager, seniorManager);
        }

        using var client = await AuthenticatedClient(seniorManager);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task List_Returns_Forbidden_For_Manager_Out_Of_Scope()
    {
        var manager = await CreateEmployeeAsync();
        var unrelatedEmployee = await CreateEmployeeAsync();
        var someoneElsesReport = await CreateEmployeeAsync();

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            // Give the manager a report — but not the target employee under test — so this
            // exercises the "manager, but target not in hierarchy" denial branch specifically,
            // not just "no reports at all".
            await AssignManagerAsync(setupClient, someoneElsesReport, manager);
        }

        using var client = await AuthenticatedClient(manager);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{unrelatedEmployee}/documents");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_Allows_HrAdministrator()
    {
        var employee = await CreateEmployeeAsync();
        using var client = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task List_Returns_Forbidden_For_Cross_Company_Caller()
    {
        var employee = await CreateEmployeeAsync();

        var crossCompanyCaller = Guid.NewGuid();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, crossCompanyCaller.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, OtherCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, crossCompanyCaller, SystemRoles.Employee, OtherCompanyId);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Get (detail)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Allows_Employee_Viewing_Own_Document()
    {
        var employee = await CreateEmployeeAsync();
        using var client = await AuthenticatedClient(employee);
        var documentId = await UploadDocumentAsync(employee);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents/{documentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_Returns_Forbidden_For_Unrelated_Peer_Employee()
    {
        var employee = await CreateEmployeeAsync();
        using var employeeClient = await AuthenticatedClient(employee);
        var documentId = await UploadDocumentAsync(employee);

        var peer = await CreateEmployeeAsync();
        using var peerClient = await AuthenticatedClient(peer);

        var response = await peerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents/{documentId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Allows_Direct_Manager()
    {
        var employee = await CreateEmployeeAsync();
        using var employeeClient = await AuthenticatedClient(employee);
        var documentId = await UploadDocumentAsync(employee);

        var manager = await CreateEmployeeAsync();
        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, employee, manager);
        }

        using var managerClient = await AuthenticatedClient(manager);

        var response = await managerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents/{documentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_Returns_Forbidden_For_Manager_Out_Of_Scope()
    {
        var employee = await CreateEmployeeAsync();
        using var employeeClient = await AuthenticatedClient(employee);
        var documentId = await UploadDocumentAsync(employee);

        var manager = await CreateEmployeeAsync();
        var someoneElsesReport = await CreateEmployeeAsync();
        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, someoneElsesReport, manager);
        }

        using var managerClient = await AuthenticatedClient(manager);

        var response = await managerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents/{documentId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Allows_HrAdministrator()
    {
        var employee = await CreateEmployeeAsync();
        using var employeeClient = await AuthenticatedClient(employee);
        var documentId = await UploadDocumentAsync(employee);

        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await hrClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents/{documentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_Returns_Forbidden_For_Cross_Company_Caller()
    {
        var employee = await CreateEmployeeAsync();
        using var employeeClient = await AuthenticatedClient(employee);
        var documentId = await UploadDocumentAsync(employee);

        var crossCompanyCaller = Guid.NewGuid();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, crossCompanyCaller.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, OtherCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, crossCompanyCaller, SystemRoles.Employee, OtherCompanyId);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents/{documentId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Download
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Download_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/documents/{Guid.NewGuid()}/download");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Download_Allows_Employee_Downloading_Own_Document()
    {
        var employee = await CreateEmployeeAsync();
        using var uploadClient = await AuthenticatedClient(employee);
        var documentId = await UploadDocumentAsync(employee);

        using var noRedirectClient = await AuthenticatedClient(employee, allowAutoRedirect: false);

        var response = await noRedirectClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents/{documentId}/download");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Download_Returns_Forbidden_For_Unrelated_Peer_Employee()
    {
        var employee = await CreateEmployeeAsync();
        using var uploadClient = await AuthenticatedClient(employee);
        var documentId = await UploadDocumentAsync(employee);

        var peer = await CreateEmployeeAsync();
        using var peerClient = await AuthenticatedClient(peer, allowAutoRedirect: false);

        var response = await peerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents/{documentId}/download");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Download_Allows_Direct_Manager()
    {
        var employee = await CreateEmployeeAsync();
        using var uploadClient = await AuthenticatedClient(employee);
        var documentId = await UploadDocumentAsync(employee);

        var manager = await CreateEmployeeAsync();
        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, employee, manager);
        }

        using var managerClient = await AuthenticatedClient(manager, allowAutoRedirect: false);

        var response = await managerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents/{documentId}/download");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Download_Returns_Forbidden_For_Manager_Out_Of_Scope()
    {
        var employee = await CreateEmployeeAsync();
        using var uploadClient = await AuthenticatedClient(employee);
        var documentId = await UploadDocumentAsync(employee);

        var manager = await CreateEmployeeAsync();
        var someoneElsesReport = await CreateEmployeeAsync();
        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, someoneElsesReport, manager);
        }

        using var managerClient = await AuthenticatedClient(manager, allowAutoRedirect: false);

        var response = await managerClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents/{documentId}/download");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Download_Allows_HrAdministrator()
    {
        var employee = await CreateEmployeeAsync();
        using var uploadClient = await AuthenticatedClient(employee);
        var documentId = await UploadDocumentAsync(employee);

        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true, allowAutoRedirect: false);

        var response = await hrClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents/{documentId}/download");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Download_Returns_Forbidden_For_Cross_Company_Caller()
    {
        var employee = await CreateEmployeeAsync();
        using var uploadClient = await AuthenticatedClient(employee);
        var documentId = await UploadDocumentAsync(employee);

        var crossCompanyCaller = Guid.NewGuid();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, crossCompanyCaller.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, OtherCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, crossCompanyCaller, SystemRoles.Employee, OtherCompanyId);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents/{documentId}/download");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Delete (HR-administrator only via the pre-existing "employee:manage" policy — not
    // self-service, so only the positive HR-administrator case plus the new cross-company
    // tenant-check regression are covered here; role-gating denial cases already live in
    // DeleteEmployeeDocumentEndpointTests)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Allows_HrAdministrator()
    {
        var employee = await CreateEmployeeAsync();
        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);
        var documentId = await UploadDocumentAsync(employee);

        var response = await hrClient.DeleteAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents/{documentId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Returns_Forbidden_For_Cross_Company_HrAdministrator()
    {
        // DOC-01: before this fix, an HR administrator's token could delete documents in a
        // different company by editing the companyId route segment — the tenant check added to
        // DeleteEmployeeDocument/Endpoint.cs closes that gap even though "employee:manage"
        // already restricted the endpoint to HR administrators.
        var employee = await CreateEmployeeAsync();
        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);
        var documentId = await UploadDocumentAsync(employee);

        var crossCompanyHrAdmin = Guid.NewGuid();
        using var crossClient = factory.CreateClient();
        crossClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, crossCompanyHrAdmin.ToString());
        crossClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, OtherCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, crossCompanyHrAdmin, SystemRoles.Employee, OtherCompanyId);
        await TestRoleSeeder.AssignRoleAsync(factory, crossCompanyHrAdmin, SystemRoles.HrAdministrator, OtherCompanyId);

        var response = await crossClient.DeleteAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/documents/{documentId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthenticatedClient(
        Guid userId, bool hrAdministrator = false, bool manager = false, bool allowAutoRedirect = true)
    {
        var client = allowAutoRedirect
            ? factory.CreateClient()
            : factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Employee, SeededCompanyId);

        if (manager)
            await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Manager, SeededCompanyId);

        if (hrAdministrator)
            await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.HrAdministrator, SeededCompanyId);

        return client;
    }

    /// <summary>
    /// Creates a real employee via the employees API and returns its id. An employee's id doubles
    /// as the identity user id for the linked account (see GetMyEmployeeHandler's `e.Id == userId`
    /// lookup), so this id is used both as the document resource's EmployeeId and as the
    /// TestAuthHandler.UserHeader value when acting "as" that employee. Mirrors
    /// LeaveResourceAuthorizationTests.CreateEmployeeAsync.
    /// </summary>
    private async Task<Guid> CreateEmployeeAsync()
    {
        using var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var unique = Guid.NewGuid().ToString("N")[..12];

        var response = await setupClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees",
            new
            {
                companyId = SeededCompanyId,
                firstName = "Test",
                lastName = $"Employee-{unique}",
                workEmail = $"doc.auth.{unique}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"DEN-{unique}",
                employmentTypeId = EmploymentTypeId,
                departmentId = DepartmentId,
                locationId = LocationId,
                positionProfileId = PositionProfileId
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        return payload!.Id;
    }

    private async Task AssignManagerAsync(HttpClient client, Guid employeeId, Guid managerId)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/manager",
            new { companyId = SeededCompanyId, id = employeeId, managerId });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// UploadEmployeeDocument is gated by the "employee:manage" policy (HrAdministrator role
    /// only — see UploadEmployeeDocument/Endpoint.cs's isManagerUpload check), so a plain
    /// employee's own client cannot upload even their own document via this endpoint; every
    /// caller here must be an HR administrator regardless of whose document is being seeded.
    /// </summary>
    private async Task<Guid> UploadDocumentAsync(Guid employeeId)
    {
        using var uploaderClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await uploaderClient.PostAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/documents",
            BuildPdfUpload());
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<UploadPayload>();

        // FakeBackgroundJobClient (see its remarks) makes ScanUploadedFileJob enqueue a no-op in
        // this test host, so a freshly-uploaded Document would otherwise sit at ScanStatus.Pending
        // forever and every download attempt would be blocked by ScanStatusAccessGuard regardless
        // of authorization — mirrors DocumentScanStatusGatingEndpointTests's direct-DbContext
        // MarkScanClean pattern to make these authorization-focused tests deterministic.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
            var document = await db.Documents.SingleAsync(d => d.Id == payload!.DocumentId);
            document.MarkScanClean(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        return payload!.EmployeeDocumentId;
    }

    private static MultipartFormDataContent BuildPdfUpload(string title = "Auth Test Doc")
    {
        var pdfBytes = new byte[1024];
        pdfBytes[0] = 0x25; pdfBytes[1] = 0x50; pdfBytes[2] = 0x44; pdfBytes[3] = 0x46; // %PDF

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(title), "Title");
        content.Add(new StringContent(AcmeContractTypeId.ToString()), "DocumentTypeId");

        var file = new ByteArrayContent(pdfBytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(file, "File", "test.pdf");

        return content;
    }

    private sealed record EmployeePayload(Guid Id);
    private sealed record UploadPayload(Guid EmployeeDocumentId, Guid DocumentId);
}
