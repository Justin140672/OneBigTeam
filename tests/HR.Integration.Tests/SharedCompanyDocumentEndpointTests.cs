using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies UploadSharedCompanyDocument / ListSharedCompanyDocuments end-to-end: the
/// shared-document:manage policy (HR-only, Company Administrator excluded unless they also
/// hold HrAdministrator, Manager excluded entirely), that a category from a different company
/// cannot be used (tenant isolation), and that a new upload always lands as Draft.
/// </summary>
public class SharedCompanyDocumentEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public SharedCompanyDocumentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Upload_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/api/companies/{Guid.NewGuid()}/shared-documents", BuildUpload());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager);
        using var client = ClientAs(companyId, userId);

        var (_, response) = await UploadAsync(client, companyId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Returns_Forbidden_For_CompanyAdministrator_Without_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.CompanyAdministrator);
        using var client = ClientAs(companyId, userId);

        var (_, response) = await UploadAsync(client, companyId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Succeeds_For_HrAdministrator_And_Creates_Draft()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientAs(companyId, userId);

        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (payload, response) = await UploadAsync(client, companyId, categoryId, title: "Remote Working Policy");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Remote Working Policy", payload!.Title);
        Assert.Equal("Draft", payload.Status);
        Assert.Equal(1, payload.VersionNumber);
    }

    [Fact]
    public async Task Upload_Returns_NotFound_When_Category_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA     = Guid.NewGuid();
        var hrInB     = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");

        // Same category id, but the caller now belongs to (and is uploading into) company B.
        using var clientB = ClientAs(companyB, hrInB);
        var (_, response) = await UploadAsync(clientB, companyB, categoryInA);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Returns_Validation_Error_When_File_Type_Not_Allowed()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");

        var form = new MultipartFormDataContent();
        form.Add(new StringContent("Bad File"), "Title");
        form.Add(new StringContent(categoryId.ToString()), "CategoryId");
        var fileContent = new ByteArrayContent([0x4D, 0x5A, 0x00, 0x00]);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        form.Add(fileContent, "File", "malware.exe");

        var response = await client.PostAsync($"/api/companies/{companyId}/shared-documents", form);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task List_Returns_Uploaded_Document_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Handbook");

        await UploadAsync(client, companyId, categoryId, title: "Employee Handbook");

        var listResponse = await client.GetAsync($"/api/companies/{companyId}/shared-documents");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var list = await listResponse.Content.ReadFromJsonAsync<ListPayload>();
        Assert.Contains(list!.Items, i => i.Title == "Employee Handbook" && i.CategoryName == "Handbook");
    }

    [Fact]
    public async Task List_Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager);
        using var client = ClientAs(companyId, userId);

        var response = await client.GetAsync($"/api/companies/{companyId}/shared-documents");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_Filters_By_Status_And_Category_And_Search()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientAs(companyId, userId);

        var policyCategory   = await CreateCategoryAsync(client, companyId, "Policy");
        var handbookCategory = await CreateCategoryAsync(client, companyId, "Handbook");

        await UploadAsync(client, companyId, policyCategory, title: "Remote Working Policy");
        await UploadAsync(client, companyId, handbookCategory, title: "Employee Handbook");

        // Filter by category — only the Policy-category document should come back.
        var byCategory = await client.GetFromJsonAsync<ListPayload>(
            $"/api/companies/{companyId}/shared-documents?categoryId={policyCategory}");
        Assert.Single(byCategory!.Items);
        Assert.Equal("Remote Working Policy", byCategory.Items[0].Title);

        // Filter by status — both are Draft (no publish endpoint exists yet), so Draft returns
        // both and Published returns none.
        var draftOnly = await client.GetFromJsonAsync<ListPayload>(
            $"/api/companies/{companyId}/shared-documents?status=Draft");
        Assert.Equal(2, draftOnly!.Items.Count);

        var publishedOnly = await client.GetFromJsonAsync<ListPayload>(
            $"/api/companies/{companyId}/shared-documents?status=Published");
        Assert.Empty(publishedOnly!.Items);

        // Search by title.
        var bySearch = await client.GetFromJsonAsync<ListPayload>(
            $"/api/companies/{companyId}/shared-documents?search=handbook");
        Assert.Single(bySearch!.Items);
        Assert.Equal("Employee Handbook", bySearch.Items[0].Title);
    }

    [Fact]
    public async Task List_Includes_UpdatedBy_Name()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");

        await UploadAsync(client, companyId, categoryId, title: "Some Policy");

        var list = await client.GetFromJsonAsync<ListPayload>($"/api/companies/{companyId}/shared-documents");
        // No employee name reader data is seeded for this ad-hoc test user, so the field is
        // populated but falls back to "Unknown" — this test asserts the field is present and
        // wired up, not the specific display name.
        Assert.NotNull(list!.Items[0].UpdatedByName);
    }

    // ── ListPublishedSharedCompanyDocuments (employee-facing simplified view) ─────

    [Fact]
    public async Task PublishedList_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/shared-documents/published");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(RolesAllowedToViewPublished))]
    public async Task PublishedList_Succeeds_For_Employee_Manager_Recruiter_And_HrAdministrator(Guid roleId)
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId);
        using var client = ClientAs(companyId, userId);

        var response = await client.GetAsync($"/api/companies/{companyId}/shared-documents/published");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public static IEnumerable<object[]> RolesAllowedToViewPublished() =>
        new[]
        {
            new object[] { SystemRoles.Employee },
            new object[] { SystemRoles.Manager },
            new object[] { SystemRoles.Recruiter },
            new object[] { SystemRoles.HrAdministrator },
        };

    [Fact]
    public async Task PublishedList_Returns_Forbidden_For_CompanyAdministrator_Without_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.CompanyAdministrator);
        using var client = ClientAs(companyId, userId);

        var response = await client.GetAsync($"/api/companies/{companyId}/shared-documents/published");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── GetSharedCompanyDocument (HR full detail) ──────────────────────────────

    [Fact]
    public async Task GetDetail_Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        var hrUserId  = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager);

        using var hrClient = ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);

        using var managerClient = ClientAs(companyId, managerId);
        var response = await managerClient.GetAsync($"/api/companies/{companyId}/shared-documents/{doc!.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDetail_Includes_VersionHistory_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId, title: "Some Policy");

        var response = await client.GetAsync($"/api/companies/{companyId}/shared-documents/{doc!.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<HrDetailPayload>();
        Assert.Single(detail!.VersionHistory);
        Assert.Equal("All Employees", detail.AudienceDescription);
    }

    // ── GetPublishedSharedCompanyDocument (employee simplified detail) ────────

    [Fact]
    public async Task GetPublishedDetail_Response_Does_Not_Contain_Management_Only_Fields()
    {
        // The core assertion for "Only management information should be visible to users with
        // document-management permission": read the raw JSON and confirm none of the
        // HR-only field names ever appear in the employee-facing response body.
        var companyId = Guid.NewGuid();
        var hrUserId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);

        using var hrClient = ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId, title: "Some Policy");
        await PublishDirectlyAsync(companyId, doc!.Id);

        using var employeeClient = ClientAs(companyId, employeeId);
        var response = await employeeClient.GetAsync($"/api/companies/{companyId}/shared-documents/published/{doc.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("versionHistory", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audienceDescription", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acknowledgementProgress", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createdBy", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("updatedBy", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPublishedDetail_Returns_NotFound_For_Draft_Document()
    {
        var companyId  = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);

        using var hrClient = ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);

        using var employeeClient = ClientAs(companyId, employeeId);
        var response = await employeeClient.GetAsync($"/api/companies/{companyId}/shared-documents/published/{doc!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── AcknowledgeSharedCompanyDocument ────────────────────────────────────────

    [Fact]
    public async Task Acknowledge_Succeeds_For_Employee_On_A_Published_Document()
    {
        var companyId  = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);

        using var hrClient = ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);
        await PublishDirectlyAsync(companyId, doc!.Id, requiresAcknowledgement: true);

        using var employeeClient = ClientAs(companyId, employeeId);
        var response = await employeeClient.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/acknowledge", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── DownloadSharedCompanyDocument ───────────────────────────────────────────

    [Fact]
    public async Task Download_Redirects_For_HrAdministrator_On_A_Draft_Document()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientAs(companyId, userId, allowAutoRedirect: false);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);

        var response = await client.GetAsync($"/api/companies/{companyId}/shared-documents/{doc!.Id}/download");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Download_Returns_NotFound_For_Employee_On_A_Draft_Document()
    {
        var companyId  = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);

        using var hrClient = ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);

        using var employeeClient = ClientAs(companyId, employeeId, allowAutoRedirect: false);
        var response = await employeeClient.GetAsync($"/api/companies/{companyId}/shared-documents/{doc!.Id}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Directly flips a document to Published via the DbContext — there is no Publish endpoint
    // yet (a known, flagged gap), so integration tests that need a Published document have no
    // way to get there through the API.
    private async Task PublishDirectlyAsync(Guid companyId, Guid documentId, bool requiresAcknowledgement = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Documents.Persistence.DocumentsDbContext>();
        var doc = await db.SharedCompanyDocuments.SingleAsync(d => d.Id == documentId && d.CompanyId == companyId);
        doc.Publish(Guid.NewGuid(), DateTimeOffset.UtcNow);
        if (requiresAcknowledgement)
        {
            doc.UpdateDetails(doc.Title, doc.Description, doc.CategoryId, doc.EffectiveDate, doc.ReviewDate,
                doc.AudienceDepartmentId, doc.AudienceLocationId, true, Guid.NewGuid(), DateTimeOffset.UtcNow);
        }
        await db.SaveChangesAsync();
    }

    private sealed record HrDetailPayload(
        IReadOnlyList<object> VersionHistory,
        string AudienceDescription);

    private async Task<Guid> CreateCategoryAsync(HttpClient client, Guid companyId, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/document-categories", new { name });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CategoryPayload>();
        return payload!.Id;
    }

    private async Task<(DocumentPayload? Payload, HttpResponseMessage Response)> UploadAsync(
        HttpClient client, Guid companyId, Guid categoryId, string title = "Test Document")
    {
        var response = await client.PostAsync($"/api/companies/{companyId}/shared-documents", BuildUpload(title, categoryId));
        DocumentPayload? payload = null;
        if (response.IsSuccessStatusCode)
            payload = await response.Content.ReadFromJsonAsync<DocumentPayload>();
        return (payload, response);
    }

    private static MultipartFormDataContent BuildUpload(string title = "Test Document", Guid? categoryId = null)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(title), "Title");
        form.Add(new StringContent((categoryId ?? Guid.NewGuid()).ToString()), "CategoryId");

        var fileContent = new ByteArrayContent(PdfBytes());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        form.Add(fileContent, "File", "policy.pdf");
        return form;
    }

    // %PDF- followed by padding, so magic-byte content validation passes.
    private static byte[] PdfBytes()
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 500];
        magic.CopyTo(bytes, 0);
        return bytes;
    }

    private HttpClient ClientAs(Guid companyId, Guid userId, bool allowAutoRedirect = true)
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
        });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private sealed record CategoryPayload(Guid Id, Guid CompanyId, string Name, bool IsActive);
    private sealed record DocumentPayload(Guid Id, string Title, string Status, int VersionNumber);
    private sealed record ListPayload(IReadOnlyList<ListItem> Items);
    private sealed record ListItem(Guid Id, string Title, string CategoryName, string Status, string UpdatedByName);
}
