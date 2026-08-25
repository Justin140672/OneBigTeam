using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Documents.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

// DOC-06: company-wide document search/filter endpoint. Access-scope resolution mirrors
// DocumentsResourceAuthorizationTests's self/manager-hierarchy/HR-administrator matrix, applied
// here across every result row instead of gating a single target employee.
[Collection("Integration")]
public class SearchEmployeeDocumentsEndpointTests(ApiWebApplicationFactory factory)
{
    private static readonly Guid AcmeContractTypeId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid EmploymentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid DepartmentId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LocationId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid PositionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // ─────────────────────────────────────────────────────────────────────────
    // Authorization / tenant checks
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(SearchUrl(SeededCompanyId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_When_Route_CompanyId_Does_Not_Match_Callers_Tenant()
    {
        var otherCompanyId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, callerId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, otherCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, callerId, SystemRoles.Employee, otherCompanyId);

        var response = await client.GetAsync(SearchUrl(SeededCompanyId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_When_NonHr_Caller_Supplies_OutOfScope_EmployeeId_Filter()
    {
        var employee = await CreateEmployeeAsync();
        var unrelatedEmployee = await CreateEmployeeAsync();
        using var client = await AuthenticatedClient(employee);

        var response = await client.GetAsync(SearchUrl(SeededCompanyId, employeeId: unrelatedEmployee));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Allows_NonHr_Caller_Supplying_Own_EmployeeId_Filter()
    {
        var employee = await CreateEmployeeAsync();
        await UploadDocumentAsync(employee, "Own Doc", "own.pdf");
        using var client = await AuthenticatedClient(employee);

        var response = await client.GetAsync(SearchUrl(SeededCompanyId, employeeId: employee));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SearchPayload>();
        Assert.Single(payload!.Items);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tenant isolation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Never_Returns_Documents_Belonging_To_A_Different_Company()
    {
        var employeeInAcme = await CreateEmployeeAsync();
        await UploadDocumentAsync(employeeInAcme, "Acme Doc", "acme.pdf");

        await UploadDocumentInOtherCompanyAsync("Other Co Doc");

        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await hrClient.GetAsync(SearchUrl(SeededCompanyId));
        var payload = await response.Content.ReadFromJsonAsync<SearchPayload>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(payload!.Items, i => i.Title == "Other Co Doc");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Permission scoping
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HrAdministrator_Sees_Documents_Company_Wide()
    {
        var employeeA = await CreateEmployeeAsync();
        var employeeB = await CreateEmployeeAsync();
        await UploadDocumentAsync(employeeA, "Doc A", "a.pdf");
        await UploadDocumentAsync(employeeB, "Doc B", "b.pdf");

        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await hrClient.GetAsync(SearchUrl(SeededCompanyId));
        var payload = await response.Content.ReadFromJsonAsync<SearchPayload>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(payload!.Items, i => i.Title == "Doc A");
        Assert.Contains(payload.Items, i => i.Title == "Doc B");
    }

    [Fact]
    public async Task Manager_Sees_Only_Their_Hierarchys_Documents()
    {
        var manager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();
        var unrelated = await CreateEmployeeAsync();

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        await UploadDocumentAsync(report, "Reports Doc", "report.pdf");
        await UploadDocumentAsync(manager, "Managers Own Doc", "manager.pdf");
        await UploadDocumentAsync(unrelated, "Unrelated Doc", "unrelated.pdf");

        using var managerClient = await AuthenticatedClient(manager);

        var response = await managerClient.GetAsync(SearchUrl(SeededCompanyId));
        var payload = await response.Content.ReadFromJsonAsync<SearchPayload>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(payload!.Items, i => i.Title == "Reports Doc");
        Assert.Contains(payload.Items, i => i.Title == "Managers Own Doc");
        Assert.DoesNotContain(payload.Items, i => i.Title == "Unrelated Doc");
    }

    [Fact]
    public async Task Plain_Employee_Sees_Only_Their_Own_Documents()
    {
        var employee = await CreateEmployeeAsync();
        var peer = await CreateEmployeeAsync();

        await UploadDocumentAsync(employee, "My Doc", "mine.pdf");
        await UploadDocumentAsync(peer, "Peer Doc", "peer.pdf");

        using var client = await AuthenticatedClient(employee);

        var response = await client.GetAsync(SearchUrl(SeededCompanyId));
        var payload = await response.Content.ReadFromJsonAsync<SearchPayload>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(payload!.Items);
        Assert.Equal("My Doc", payload.Items[0].Title);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Archived exclusion
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Excludes_Archived_Documents_By_Default_For_HrAdministrator()
    {
        var employee = await CreateEmployeeAsync();
        var (_, employeeDocumentId) = await UploadDocumentAsync(employee, "Archived Doc", "archived.pdf");
        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);
        await ArchiveAsync(hrClient, employee, employeeDocumentId);

        var response = await hrClient.GetAsync(SearchUrl(SeededCompanyId));
        var payload = await response.Content.ReadFromJsonAsync<SearchPayload>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(payload!.Items, i => i.Title == "Archived Doc");
    }

    [Fact]
    public async Task Includes_Archived_Documents_When_Requested_By_HrAdministrator()
    {
        var employee = await CreateEmployeeAsync();
        var (_, employeeDocumentId) = await UploadDocumentAsync(employee, "Archived Doc", "archived.pdf");
        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);
        await ArchiveAsync(hrClient, employee, employeeDocumentId);

        var response = await hrClient.GetAsync(SearchUrl(SeededCompanyId, includeArchived: true));
        var payload = await response.Content.ReadFromJsonAsync<SearchPayload>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(payload!.Items, i => i.Title == "Archived Doc");
    }

    [Fact]
    public async Task IncludeArchived_Has_No_Effect_For_NonHr_Caller()
    {
        var employee = await CreateEmployeeAsync();
        var (_, employeeDocumentId) = await UploadDocumentAsync(employee, "Archived Doc", "archived.pdf");
        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);
        await ArchiveAsync(hrClient, employee, employeeDocumentId);

        using var employeeClient = await AuthenticatedClient(employee);

        var response = await employeeClient.GetAsync(SearchUrl(SeededCompanyId, includeArchived: true));
        var payload = await response.Content.ReadFromJsonAsync<SearchPayload>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(payload!.Items, i => i.Title == "Archived Doc");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Combined filters, pagination and ordering
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Combined_Filters_Return_Paginated_And_Correctly_Ordered_Results()
    {
        var employee = await CreateEmployeeAsync();

        await UploadDocumentAsync(employee, "Old Right To Work", "old-rtw.pdf");
        await Task.Delay(15);
        await UploadDocumentAsync(employee, "Middle Right To Work", "mid-rtw.pdf");
        await Task.Delay(15);
        await UploadDocumentAsync(employee, "New Right To Work", "new-rtw.pdf");
        // Not matching the search text — should never appear regardless of pagination.
        await UploadDocumentAsync(employee, "Passport", "passport.pdf");

        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var page1Response = await hrClient.GetAsync(
            SearchUrl(SeededCompanyId, searchText: "right to work", documentTypeId: AcmeContractTypeId, pageNumber: 1, pageSize: 2));
        var page1 = await page1Response.Content.ReadFromJsonAsync<SearchPayload>();

        Assert.Equal(HttpStatusCode.OK, page1Response.StatusCode);
        Assert.Equal(3, page1!.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(["New Right To Work", "Middle Right To Work"], page1.Items.Select(i => i.Title));

        var page2Response = await hrClient.GetAsync(
            SearchUrl(SeededCompanyId, searchText: "right to work", documentTypeId: AcmeContractTypeId, pageNumber: 2, pageSize: 2));
        var page2 = await page2Response.Content.ReadFromJsonAsync<SearchPayload>();

        Assert.Single(page2!.Items);
        Assert.Equal("Old Right To Work", page2.Items[0].Title);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthenticatedClient(Guid userId, bool hrAdministrator = false)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Employee, SeededCompanyId);

        if (hrAdministrator)
            await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.HrAdministrator, SeededCompanyId);

        return client;
    }

    /// <summary>
    /// Creates a real employee via the employees API and returns its id, which doubles as the
    /// identity user id for the linked account — mirrors
    /// DocumentsResourceAuthorizationTests.CreateEmployeeAsync.
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
                workEmail = $"doc.search.{unique}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"DSN-{unique}",
                employmentTypeId = EmploymentTypeId,
                departmentId = DepartmentId,
                locationId = LocationId,
                positionProfileId = PositionProfileId
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        return payload!.Id;
    }

    /// <summary>
    /// Sets up a wholly separate company (own subscription, reference data, employee, document
    /// type and HR-administrator caller) and uploads a single document into it — used to prove
    /// the search endpoint never leaks another tenant's documents even though employee/company
    /// GUIDs are otherwise randomly generated and could theoretically collide.
    /// </summary>
    private async Task UploadDocumentInOtherCompanyAsync(string title)
    {
        var otherCompanyId = await CompanyTestSeeder.CreateCompanyAsync(factory);

        var uploaderId = Guid.NewGuid();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, uploaderId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, otherCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, uploaderId, SystemRoles.Employee, otherCompanyId);
        await TestRoleSeeder.AssignRoleAsync(factory, uploaderId, SystemRoles.HrAdministrator, otherCompanyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, otherCompanyId);

        var employeeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{otherCompanyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                otherCompanyId, refData, "Other", "Company", $"other.co.{Guid.NewGuid():N}@example.com"));
        employeeResponse.EnsureSuccessStatusCode();
        var employeeId = (await employeeResponse.Content.ReadFromJsonAsync<EmployeePayload>())!.Id;

        var docTypeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{otherCompanyId}/document-types",
            new { name = "Contract", allowEmployeeUpload = false });
        docTypeResponse.EnsureSuccessStatusCode();
        var docTypeId = (await docTypeResponse.Content.ReadFromJsonAsync<DocTypePayload>())!.Id;

        var response = await client.PostAsync(
            $"/api/companies/{otherCompanyId}/employees/{employeeId}/documents",
            BuildPdfUpload(docTypeId, title));
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<UploadPayload>();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        var document = await db.Documents.SingleAsync(d => d.Id == payload!.DocumentId);
        document.MarkScanClean(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
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
    /// only), so every upload here goes through a fresh HR-administrator client regardless of
    /// whose document is being seeded — mirrors DocumentsResourceAuthorizationTests.UploadDocumentAsync.
    /// The freshly-uploaded Document is force-marked clean so ScanStatusAccessGuard never blocks
    /// the search endpoint's results deterministically in this test host (FakeBackgroundJobClient
    /// makes ScanUploadedFileJob a no-op).
    /// </summary>
    private async Task<(Guid DocumentId, Guid EmployeeDocumentId)> UploadDocumentAsync(Guid employeeId, string title, string fileName)
    {
        using var uploaderClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await uploaderClient.PostAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/documents",
            BuildPdfUpload(AcmeContractTypeId, title));
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<UploadPayload>();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
            var document = await db.Documents.SingleAsync(d => d.Id == payload!.DocumentId);
            document.MarkScanClean(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        return (payload!.DocumentId, payload.EmployeeDocumentId);
    }

    private async Task ArchiveAsync(HttpClient hrClient, Guid employeeId, Guid employeeDocumentId)
    {
        var response = await hrClient.DeleteAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/documents/{employeeDocumentId}");
        response.EnsureSuccessStatusCode();
    }

    private static MultipartFormDataContent BuildPdfUpload(Guid documentTypeId, string title)
    {
        var pdfBytes = new byte[1024];
        pdfBytes[0] = 0x25; pdfBytes[1] = 0x50; pdfBytes[2] = 0x44; pdfBytes[3] = 0x46; // %PDF

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(title), "Title");
        content.Add(new StringContent(documentTypeId.ToString()), "DocumentTypeId");

        var file = new ByteArrayContent(pdfBytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(file, "File", "test.pdf");

        return content;
    }

    private static string SearchUrl(
        Guid companyId,
        string? searchText = null,
        Guid? documentTypeId = null,
        Guid? employeeId = null,
        bool? includeArchived = null,
        int? pageNumber = null,
        int? pageSize = null)
    {
        var parameters = new List<string>();
        if (searchText is not null) parameters.Add($"searchText={Uri.EscapeDataString(searchText)}");
        if (documentTypeId is not null) parameters.Add($"documentTypeId={documentTypeId}");
        if (employeeId is not null) parameters.Add($"employeeId={employeeId}");
        if (includeArchived is not null) parameters.Add($"includeArchived={includeArchived.Value.ToString().ToLowerInvariant()}");
        if (pageNumber is not null) parameters.Add($"pageNumber={pageNumber}");
        if (pageSize is not null) parameters.Add($"pageSize={pageSize}");

        var url = $"/api/companies/{companyId}/documents/search";
        return parameters.Count == 0 ? url : $"{url}?{string.Join('&', parameters)}";
    }

    private sealed record EmployeePayload(Guid Id);
    private sealed record DocTypePayload(Guid Id);
    private sealed record UploadPayload(Guid EmployeeDocumentId, Guid DocumentId);
    private sealed record SearchPayload(IReadOnlyList<SearchItem> Items, int TotalCount, int PageNumber, int PageSize, int TotalPages);
    private sealed record SearchItem(Guid EmployeeDocumentId, Guid EmployeeId, string EmployeeName, string Title);
}
