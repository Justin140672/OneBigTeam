using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
[Collection("Integration")]
public class SharedCompanyDocumentEndpointTests
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
        using var client = await ClientAs(companyId, userId);

        var (_, response) = await UploadAsync(client, companyId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Returns_Forbidden_For_CompanyAdministrator_Without_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.CompanyAdministrator);
        using var client = await ClientAs(companyId, userId);

        var (_, response) = await UploadAsync(client, companyId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Succeeds_For_HrAdministrator_And_Creates_Draft()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (payload, response) = await UploadAsync(client, companyId, categoryId, title: "Remote Working Policy");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Remote Working Policy", payload!.Title);
        Assert.Equal("Draft", payload.Status);
        Assert.Equal(1, payload.VersionNumber);
    }

    [Fact]
    public async Task Upload_With_ReviewFrequency_RoundTrips_Via_GetDetail()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, response) = await UploadAsync(
            client, companyId, categoryId, title: "Remote Working Policy",
            reviewFrequency: "Monthly", reviewDate: new DateOnly(2027, 1, 1));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var detailResponse = await client.GetAsync($"/api/companies/{companyId}/shared-documents/{doc!.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        var detail = await detailResponse.Content.ReadFromJsonAsync<ReviewFrequencyDetailPayload>();
        Assert.Equal("Monthly", detail!.ReviewFrequency);
    }

    [Fact]
    public async Task Upload_With_ReviewFrequency_And_No_ReviewDate_Returns_BadRequest()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (_, response) = await UploadAsync(
            client, companyId, categoryId, title: "Remote Working Policy", reviewFrequency: "Monthly");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Upload_With_ReviewOwnerEmployeeId_RoundTrips_Via_GetDetail()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var reviewOwnerId = Guid.NewGuid();
        await CreateActiveEmployeeAsync(companyId, reviewOwnerId);

        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, response) = await UploadAsync(
            client, companyId, categoryId, title: "Remote Working Policy",
            reviewOwnerEmployeeId: reviewOwnerId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var detailResponse = await client.GetAsync($"/api/companies/{companyId}/shared-documents/{doc!.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        var detail = await detailResponse.Content.ReadFromJsonAsync<ReviewFrequencyDetailPayload>();
        Assert.Equal(reviewOwnerId,        detail!.ReviewOwnerEmployeeId);
        Assert.Equal("Ada Acknowledger",   detail.ReviewOwnerName);
    }

    [Fact]
    public async Task Upload_With_Unknown_ReviewOwnerEmployeeId_Returns_NotFound()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (_, response) = await UploadAsync(
            client, companyId, categoryId, title: "Remote Working Policy",
            reviewOwnerEmployeeId: Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");

        // Same category id, but the caller now belongs to (and is uploading into) company B.
        using var clientB = await ClientAs(companyB, hrInB);
        var (_, response) = await UploadAsync(clientB, companyB, categoryInA);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Returns_Validation_Error_When_File_Type_Not_Allowed()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
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
        using var client = await ClientAs(companyId, userId);
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
        using var client = await ClientAs(companyId, userId);

        var response = await client.GetAsync($"/api/companies/{companyId}/shared-documents");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/shared-documents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_Includes_UpdatedBy_Name()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");

        await UploadAsync(client, companyId, categoryId, title: "Some Policy");

        var list = await client.GetFromJsonAsync<ListPayload>($"/api/companies/{companyId}/shared-documents");
        // No employee name reader data is seeded for this ad-hoc test user, so the field is
        // populated but falls back to "Unknown" — this test asserts the field is present and
        // wired up, not the specific display name.
        Assert.NotNull(list!.Items[0].UpdatedByName);
    }

    [Fact]
    public async Task List_Includes_ReviewFrequency_And_ReviewOwner_Name()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");

        var reviewOwnerId = Guid.NewGuid();
        await CreateActiveEmployeeAsync(companyId, reviewOwnerId);

        var (doc, response) = await UploadAsync(
            client, companyId, categoryId, title: "Quarterly Reviewed Policy",
            reviewFrequency: "Quarterly", reviewDate: new DateOnly(2027, 1, 1),
            reviewOwnerEmployeeId: reviewOwnerId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var list = await client.GetFromJsonAsync<ListPayload>($"/api/companies/{companyId}/shared-documents");
        var item = Assert.Single(list!.Items, i => i.Id == doc!.Id);

        Assert.Equal("Quarterly",       item.ReviewFrequency);
        Assert.Equal(reviewOwnerId,     item.ReviewOwnerEmployeeId);
        Assert.Equal("Ada Acknowledger", item.ReviewOwnerName);
    }

    [Fact]
    public async Task List_Excludes_Documents_From_Other_Companies()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA     = Guid.NewGuid();
        var hrInB     = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        await UploadAsync(clientA, companyA, categoryInA, title: "Company A Only Policy");

        // Company B's HR administrator lists documents scoped to company B — company A's
        // document must not leak into the results even though no filter would otherwise
        // exclude it by title/category.
        using var clientB = await ClientAs(companyB, hrInB);
        var list = await clientB.GetFromJsonAsync<ListPayload>($"/api/companies/{companyB}/shared-documents");

        Assert.DoesNotContain(list!.Items, i => i.Title == "Company A Only Policy");
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
        using var client = await ClientAs(companyId, userId);

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
        using var client = await ClientAs(companyId, userId);

        var response = await client.GetAsync($"/api/companies/{companyId}/shared-documents/published");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PublishedList_Excludes_Documents_From_Other_Companies()
    {
        var companyA    = Guid.NewGuid();
        var companyB    = Guid.NewGuid();
        var hrInA       = Guid.NewGuid();
        var employeeInB = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeInB, SystemRoles.Employee);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        var (doc, _) = await UploadAsync(clientA, companyA, categoryInA, title: "Company A Published Policy");
        await PublishDirectlyAsync(companyA, doc!.Id);

        using var clientB = await ClientAs(companyB, employeeInB);
        var list = await clientB.GetFromJsonAsync<ListPayload>($"/api/companies/{companyB}/shared-documents/published");

        Assert.DoesNotContain(list!.Items, i => i.Title == "Company A Published Policy");
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

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);

        using var managerClient = await ClientAs(companyId, managerId);
        var response = await managerClient.GetAsync($"/api/companies/{companyId}/shared-documents/{doc!.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDetail_Includes_VersionHistory_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId, title: "Some Policy");

        var response = await client.GetAsync($"/api/companies/{companyId}/shared-documents/{doc!.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<HrDetailPayload>();
        Assert.Single(detail!.VersionHistory);
        Assert.Equal("All Employees", detail.AudienceDescription);
    }

    [Fact]
    public async Task GetDetail_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA     = Guid.NewGuid();
        var hrInB     = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        var (doc, _) = await UploadAsync(clientA, companyA, categoryInA);

        using var clientB = await ClientAs(companyB, hrInB);
        var response = await clientB.GetAsync($"/api/companies/{companyB}/shared-documents/{doc!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId, title: "Some Policy");
        await PublishDirectlyAsync(companyId, doc!.Id);

        using var employeeClient = await ClientAs(companyId, employeeId);
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

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);

        using var employeeClient = await ClientAs(companyId, employeeId);
        var response = await employeeClient.GetAsync($"/api/companies/{companyId}/shared-documents/published/{doc!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPublishedDetail_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        var companyA    = Guid.NewGuid();
        var companyB    = Guid.NewGuid();
        var hrInA       = Guid.NewGuid();
        var employeeInB = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeInB, SystemRoles.Employee);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        var (doc, _) = await UploadAsync(clientA, companyA, categoryInA);
        await PublishDirectlyAsync(companyA, doc!.Id);

        using var clientB = await ClientAs(companyB, employeeInB);
        var response = await clientB.GetAsync($"/api/companies/{companyB}/shared-documents/published/{doc.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPublishedDetail_Returns_Forbidden_For_CompanyAdministrator_Without_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.CompanyAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.GetAsync($"/api/companies/{companyId}/shared-documents/published/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);
        await PublishDirectlyAsync(companyId, doc!.Id, requiresAcknowledgement: true);

        using var employeeClient = await ClientAs(companyId, employeeId);
        var response = await employeeClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/acknowledge", new { Confirmed = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Acknowledge_Returns_Forbidden_For_CompanyAdministrator_Without_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.CompanyAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{Guid.NewGuid()}/acknowledge", EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Acknowledge_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        var companyA    = Guid.NewGuid();
        var companyB    = Guid.NewGuid();
        var hrInA       = Guid.NewGuid();
        var employeeInB = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeInB, SystemRoles.Employee);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        var (doc, _) = await UploadAsync(clientA, companyA, categoryInA);
        await PublishDirectlyAsync(companyA, doc!.Id, requiresAcknowledgement: true);

        using var clientB = await ClientAs(companyB, employeeInB);
        var response = await clientB.PostAsJsonAsync(
            $"/api/companies/{companyB}/shared-documents/{doc.Id}/acknowledge", new { Confirmed = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Acknowledge_With_Confirmed_False_Returns_UnprocessableEntity_With_Validator_Message()
    {
        var companyId  = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);
        await PublishDirectlyAsync(companyId, doc!.Id, requiresAcknowledgement: true);

        using var employeeClient = await ClientAs(companyId, employeeId);
        var response = await employeeClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/acknowledge", new { Confirmed = false });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(
            "You must confirm that you have read and understood this document before it can be acknowledged.", body);
    }

    [Fact]
    public async Task Acknowledge_Omitting_Confirmed_Defaults_To_False_And_Returns_UnprocessableEntity()
    {
        var companyId  = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);
        await PublishDirectlyAsync(companyId, doc!.Id, requiresAcknowledgement: true);

        using var employeeClient = await ClientAs(companyId, employeeId);
        var response = await employeeClient.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/acknowledge", EmptyJson());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Acknowledge_With_Confirmed_True_Succeeds_And_Persists_IsConfirmed()
    {
        var companyId  = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);
        await PublishDirectlyAsync(companyId, doc!.Id, requiresAcknowledgement: true);

        using var employeeClient = await ClientAs(companyId, employeeId);
        var response = await employeeClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/acknowledge", new { Confirmed = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Documents.Persistence.DocumentsDbContext>();
        var saved = await db.SharedCompanyDocumentAcknowledgements
            .SingleAsync(a => a.SharedCompanyDocumentId == doc.Id && a.EmployeeId == employeeId);

        Assert.True(saved.IsConfirmed);
    }

    // ── GetSharedCompanyDocumentAuditHistory ────────────────────────────────────

    [Fact]
    public async Task AuditHistory_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/shared-documents/{Guid.NewGuid()}/audit-history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuditHistory_Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);

        using var managerClient = await ClientAs(companyId, managerId);
        var response = await managerClient.GetAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/audit-history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuditHistory_Returns_History_Items_After_An_Acknowledgement_Was_Made()
    {
        var companyId  = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);
        await PublishDirectlyAsync(companyId, doc!.Id, requiresAcknowledgement: true);

        using var employeeClient = await ClientAs(companyId, employeeId);
        var ackResponse = await employeeClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/acknowledge", new { Confirmed = true });
        Assert.Equal(HttpStatusCode.OK, ackResponse.StatusCode);

        var response = await hrClient.GetAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/audit-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuditHistoryPayload>();
        Assert.Contains(payload!.Items, i => i.Action.Contains("acknowledged", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AuditHistory_Returns_Empty_Items_For_A_Document_With_No_Audit_Entries()
    {
        // The handler always returns 200 with an empty history list rather than 404 when no
        // audit entries exist for the given document id — it does not verify the document
        // itself exists, only that no matching audit rows were found.
        var companyId = Guid.NewGuid();
        var hrUserId  = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        using var hrClient = await ClientAs(companyId, hrUserId);

        var response = await hrClient.GetAsync(
            $"/api/companies/{companyId}/shared-documents/{Guid.NewGuid()}/audit-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuditHistoryPayload>();
        Assert.Empty(payload!.Items);
    }

    private sealed record AuditHistoryPayload(IReadOnlyList<AuditHistoryItemPayload> Items);
    private sealed record AuditHistoryItemPayload(DateTimeOffset OccurredAt, string Action, string User);

    // ── DownloadSharedCompanyDocument ───────────────────────────────────────────

    [Fact]
    public async Task Download_Redirects_For_HrAdministrator_On_A_Draft_Document()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId, allowAutoRedirect: false);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);

        var response = await client.GetAsync($"/api/companies/{companyId}/shared-documents/{doc!.Id}/download");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Download_Returns_Forbidden_When_Tenant_Claim_Does_Not_Match_Route()
    {
        var companyId       = Guid.NewGuid();
        var differentCompany = Guid.NewGuid();
        var userId          = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var uploadClient = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(uploadClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(uploadClient, companyId, categoryId);

        // Same user, but the tenant header on this client claims a different company than the
        // one in the route — must be rejected before the document lookup even runs.
        using var mismatchedClient = await ClientAs(differentCompany, userId, allowAutoRedirect: false);
        var response = await mismatchedClient.GetAsync($"/api/companies/{companyId}/shared-documents/{doc!.Id}/download");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Download_Returns_NotFound_For_Employee_On_A_Draft_Document()
    {
        var companyId  = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);

        using var employeeClient = await ClientAs(companyId, employeeId, allowAutoRedirect: false);
        var response = await employeeClient.GetAsync($"/api/companies/{companyId}/shared-documents/{doc!.Id}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response      = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/shared-documents/{Guid.NewGuid()}/download");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Download_Returns_NotFound_While_Document_Scan_Is_Pending()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId, allowAutoRedirect: false);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Documents.Persistence.DocumentsDbContext>();
            var stored = await db.SharedCompanyDocuments.SingleAsync(d => d.Id == doc!.Id);
            stored.MarkScanning(DateTimeOffset.UtcNow); // UploadAsync helper marks Clean by default; revert to a not-yet-Clean state.
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/shared-documents/{doc!.Id}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── UpdateSharedCompanyDocumentMetadata ─────────────────────────────────────

    [Fact]
    public async Task UpdateMetadata_Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);

        using var managerClient = await ClientAs(companyId, managerId);
        var response = await managerClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}",
            new { Title = "New Title", CategoryId = categoryId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMetadata_Succeeds_For_HrAdministrator_And_Updates_Fields()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId, title: "Old Title");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}",
            new
            {
                Title         = "Updated Policy Title",
                Description   = "Updated description",
                CategoryId    = categoryId,
                EffectiveDate = new DateOnly(2026, 9, 1),
                ReviewDate    = new DateOnly(2027, 9, 1),
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UpdatePayload>();
        Assert.Equal("Updated Policy Title", payload!.Title);
        Assert.Equal(1, payload.VersionNumber);
    }

    [Fact]
    public async Task UpdateMetadata_With_Custom_ReviewFrequency_RoundTrips_Via_GetDetail()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId, title: "Old Title");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}",
            new
            {
                Title                       = "Updated Policy Title",
                CategoryId                  = categoryId,
                ReviewFrequency             = "Custom",
                CustomReviewFrequencyMonths = 6,
                ReviewDate                  = new DateOnly(2027, 1, 1),
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detailResponse = await client.GetAsync($"/api/companies/{companyId}/shared-documents/{doc.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        var detail = await detailResponse.Content.ReadFromJsonAsync<ReviewFrequencyDetailPayload>();
        Assert.Equal("Custom", detail!.ReviewFrequency);
        Assert.Equal(6,        detail.CustomReviewFrequencyMonths);
    }

    [Fact]
    public async Task UpdateMetadata_With_Unknown_ReviewOwnerEmployeeId_Returns_NotFound()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId, title: "Old Title");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}",
            new
            {
                Title                 = "Updated Policy Title",
                CategoryId            = categoryId,
                ReviewOwnerEmployeeId = Guid.NewGuid(),
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMetadata_With_ReviewFrequency_And_No_ReviewDate_Returns_BadRequest()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId, title: "Old Title");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}",
            new
            {
                Title           = "Updated Policy Title",
                CategoryId      = categoryId,
                ReviewFrequency = "Monthly",
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMetadata_Returns_NotFound_When_Category_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA     = Guid.NewGuid();
        var hrInB     = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        var (doc, _) = await UploadAsync(clientA, companyA, categoryInA);

        using var clientB = await ClientAs(companyB, hrInB);
        var categoryInB = await CreateCategoryAsync(clientB, companyB, "Policy");

        // Caller is HR in company A, editing a document in company A, but supplying a category
        // id that belongs to company B.
        var response = await clientA.PutAsJsonAsync(
            $"/api/companies/{companyA}/shared-documents/{doc!.Id}",
            new { Title = "Title", CategoryId = categoryInB });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMetadata_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA     = Guid.NewGuid();
        var hrInB     = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        var (doc, _) = await UploadAsync(clientA, companyA, categoryInA);

        using var clientB = await ClientAs(companyB, hrInB);
        var categoryInB = await CreateCategoryAsync(clientB, companyB, "Policy");

        var response = await clientB.PutAsJsonAsync(
            $"/api/companies/{companyB}/shared-documents/{doc!.Id}",
            new { Title = "Title", CategoryId = categoryInB });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record UpdatePayload(Guid Id, string Title, int VersionNumber, string Status);

    // ── UpdateSharedCompanyDocumentAudience ─────────────────────────────────────

    [Fact]
    public async Task UpdateAudience_Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);

        using var managerClient = await ClientAs(companyId, managerId);
        var response = await managerClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/audience",
            new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAudience_Succeeds_For_HrAdministrator_And_Scopes_To_A_Department()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);
        var departmentId = await SeedDepartmentAsync(companyId, "Engineering");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/audience",
            new { AudienceDepartmentIds = new[] { departmentId } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AudiencePayload>();
        Assert.Equal([departmentId], payload!.AudienceDepartmentIds);
        Assert.Equal("Departments: Engineering", payload.AudienceDescription);
    }

    [Fact]
    public async Task UpdateAudience_Returns_NotFound_When_Department_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/audience",
            new { AudienceDepartmentIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAudience_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA     = Guid.NewGuid();
        var hrInB     = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        var (doc, _) = await UploadAsync(clientA, companyA, categoryInA);

        using var clientB = await ClientAs(companyB, hrInB);
        var response = await clientB.PutAsJsonAsync(
            $"/api/companies/{companyB}/shared-documents/{doc!.Id}/audience",
            new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record AudiencePayload(
        Guid Id, Guid CompanyId,
        IReadOnlyList<Guid> AudienceDepartmentIds, IReadOnlyList<Guid> AudienceLocationIds,
        IReadOnlyList<Guid> AudiencePositionProfileIds, IReadOnlyList<Guid> AudienceEmployeeIds,
        string AudienceDescription);

    // ── PublishSharedCompanyDocument ─────────────────────────────────────────────

    [Fact]
    public async Task Publish_Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);

        using var managerClient = await ClientAs(companyId, managerId);
        var response = await managerClient.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/publish", EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Publish_Succeeds_For_HrAdministrator_And_Changes_Status_To_Published()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId, title: "Remote Working Policy");

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/publish", EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PublishPayload>();
        Assert.Equal("Published", payload!.Status);
        Assert.Equal(userId, payload.PublishedBy);

        // A published document is now visible to employees via the published-list endpoint.
        var employeeId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);
        using var employeeClient = await ClientAs(companyId, employeeId);
        var list = await employeeClient.GetFromJsonAsync<ListPayload>($"/api/companies/{companyId}/shared-documents/published");
        Assert.Contains(list!.Items, i => i.Title == "Remote Working Policy");
    }

    [Fact]
    public async Task Publish_Returns_Conflict_When_Document_Already_Published()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);

        await client.PostAsync($"/api/companies/{companyId}/shared-documents/{doc!.Id}/publish", EmptyJson());
        var response = await client.PostAsync($"/api/companies/{companyId}/shared-documents/{doc.Id}/publish", EmptyJson());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Publish_Returns_NotFound_For_Unknown_Document()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{Guid.NewGuid()}/publish", EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Publish_Returns_Validation_When_Acknowledgement_Required_Without_DueDate()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);

        await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/acknowledgement-settings",
            new { RequiresAcknowledgement = true, AcknowledgementStatement = "I confirm I have read this." });

        var response = await client.PostAsync($"/api/companies/{companyId}/shared-documents/{doc.Id}/publish", EmptyJson());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Publish_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA     = Guid.NewGuid();
        var hrInB     = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        var (doc, _) = await UploadAsync(clientA, companyA, categoryInA);

        using var clientB = await ClientAs(companyB, hrInB);
        var response = await clientB.PostAsync(
            $"/api/companies/{companyB}/shared-documents/{doc!.Id}/publish", EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── UpdateSharedCompanyDocumentAcknowledgementSettings ──────────────────────

    [Fact]
    public async Task UpdateAcknowledgementSettings_Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);

        using var managerClient = await ClientAs(companyId, managerId);
        var response = await managerClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/acknowledgement-settings",
            new { RequiresAcknowledgement = true, AcknowledgementDueDate = "2027-01-01" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAcknowledgementSettings_Succeeds_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/acknowledgement-settings",
            new
            {
                RequiresAcknowledgement = true,
                AcknowledgementDueDate = "2027-01-01",
                AcknowledgementStatement = "I confirm I have read the updated expenses policy.",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AcknowledgementSettingsPayload>();
        Assert.True(payload!.RequiresAcknowledgement);
        Assert.Equal(new DateOnly(2027, 1, 1), payload.AcknowledgementDueDate);
        Assert.Equal("I confirm I have read the updated expenses policy.", payload.AcknowledgementStatement);

        // Publish now succeeds, since the required due date has been set.
        var publishResponse = await client.PostAsync($"/api/companies/{companyId}/shared-documents/{doc.Id}/publish", EmptyJson());
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateAcknowledgementSettings_Returns_NotFound_For_Unknown_Document()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{Guid.NewGuid()}/acknowledgement-settings",
            new { RequiresAcknowledgement = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAcknowledgementSettings_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA     = Guid.NewGuid();
        var hrInB     = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        var (doc, _) = await UploadAsync(clientA, companyA, categoryInA);

        using var clientB = await ClientAs(companyB, hrInB);
        var response = await clientB.PutAsJsonAsync(
            $"/api/companies/{companyB}/shared-documents/{doc!.Id}/acknowledgement-settings",
            new { RequiresAcknowledgement = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record AcknowledgementSettingsPayload(
        Guid Id, Guid CompanyId, bool RequiresAcknowledgement,
        DateOnly? AcknowledgementDueDate, string? AcknowledgementStatement);

    // ── GetSharedCompanyDocumentAcknowledgementProgress ─────────────────────────

    [Fact]
    public async Task AcknowledgementProgress_Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);
        await PublishDirectlyAsync(companyId, doc!.Id, requiresAcknowledgement: true);

        using var managerClient = await ClientAs(companyId, managerId);
        var response = await managerClient.GetAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/acknowledgement-progress");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AcknowledgementProgress_Succeeds_For_HrAdministrator_With_Summary_Counts()
    {
        var companyId  = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId, title: "Remote Working Policy");
        await PublishDirectlyAsync(companyId, doc!.Id, requiresAcknowledgement: true);

        // GetSharedCompanyDocumentAcknowledgementProgress's eligible-employee lookup
        // (EmployeeAudienceReader.GetEligibleEmployeeIdsAsync) queries real, Active rows in the
        // Employees module — a bare role/claims assignment via TestRoleSeeder isn't enough for
        // this employee to show up in the progress report, unlike the simpler per-employee
        // audience check used when viewing published documents. Seed a real Active Employee with
        // Id == employeeId so they're counted as eligible.
        await CreateActiveEmployeeAsync(companyId, employeeId);

        using var employeeClient = await ClientAs(companyId, employeeId);
        await employeeClient.PostAsJsonAsync($"/api/companies/{companyId}/shared-documents/{doc.Id}/acknowledge", new { Confirmed = true });

        var response = await hrClient.GetAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/acknowledgement-progress");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AcknowledgementProgressPayload>();
        Assert.Equal("Remote Working Policy", payload!.DocumentTitle);
        Assert.True(payload.TotalAssigned >= payload.AcknowledgedCount);
        Assert.Equal(payload.TotalAssigned, payload.AcknowledgedCount + payload.OutstandingCount + payload.OverdueCount);
        Assert.Contains(payload.Items, i => i.EmployeeId == employeeId && i.Status == "Acknowledged");

        var acknowledgedItem = payload.Items.Single(i => i.EmployeeId == employeeId);
        Assert.Equal(1, acknowledgedItem.VersionNumber);
        Assert.False(string.IsNullOrWhiteSpace(acknowledgedItem.AcknowledgementStatement));

        var outstandingItem = payload.Items.SingleOrDefault(i => i.Status != "Acknowledged");
        if (outstandingItem is not null)
        {
            Assert.Null(outstandingItem.VersionNumber);
            Assert.Null(outstandingItem.AcknowledgementStatement);
        }
    }

    [Fact]
    public async Task AcknowledgementProgress_Returns_UnprocessableEntity_When_Document_Does_Not_Require_Acknowledgement()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);
        await PublishDirectlyAsync(companyId, doc!.Id, requiresAcknowledgement: false);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/acknowledgement-progress");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AcknowledgementProgress_Returns_NotFound_For_Unknown_Document()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/shared-documents/{Guid.NewGuid()}/acknowledgement-progress");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AcknowledgementProgress_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA     = Guid.NewGuid();
        var hrInB     = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        var (doc, _) = await UploadAsync(clientA, companyA, categoryInA);
        await PublishDirectlyAsync(companyA, doc!.Id, requiresAcknowledgement: true);

        using var clientB = await ClientAs(companyB, hrInB);
        var response = await clientB.GetAsync(
            $"/api/companies/{companyB}/shared-documents/{doc.Id}/acknowledgement-progress");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record AcknowledgementProgressPayload(
        Guid DocumentId, string DocumentTitle, int TotalAssigned, int AcknowledgedCount,
        int OutstandingCount, int OverdueCount, decimal AcknowledgementPercentage,
        IReadOnlyList<AcknowledgementProgressItemPayload> Items);

    private sealed record AcknowledgementProgressItemPayload(
        Guid EmployeeId, string EmployeeName, Guid? DepartmentId, string? DepartmentName,
        Guid? LocationId, string? LocationName, string Status, DateOnly? DueDate, DateTimeOffset? AcknowledgedAt,
        int? VersionNumber, string? AcknowledgementStatement);

    private sealed record PublishPayload(Guid Id, string Status, Guid PublishedBy, DateTimeOffset PublishedAt);

    // ── UploadSharedCompanyDocumentVersion ──────────────────────────────────────

    [Fact]
    public async Task UploadVersion_Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);

        using var managerClient = await ClientAs(companyId, managerId);
        var response = await managerClient.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/versions", BuildVersionUpload());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UploadVersion_Succeeds_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId, title: "Remote Working Policy");

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/versions", BuildVersionUpload());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<VersionUploadPayload>();
        Assert.Equal(2, payload!.VersionNumber);
    }

    [Fact]
    public async Task UploadVersion_Returns_NotFound_For_Unknown_Document()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{Guid.NewGuid()}/versions", BuildVersionUpload());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UploadVersion_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA     = Guid.NewGuid();
        var hrInB     = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        var (doc, _) = await UploadAsync(clientA, companyA, categoryInA);

        using var clientB = await ClientAs(companyB, hrInB);
        var response = await clientB.PostAsync(
            $"/api/companies/{companyB}/shared-documents/{doc!.Id}/versions", BuildVersionUpload());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UploadVersion_Copies_Forward_Previous_Versions_AcknowledgementStatement_When_No_Override_Given()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(
            client, companyId, categoryId, title: "Remote Working Policy",
            requiresAcknowledgement: true, acknowledgementDueDate: new DateOnly(2027, 1, 1),
            acknowledgementStatement: "I confirm I have read the original policy.");

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/versions",
            BuildVersionUpload(requiresReacknowledgement: true));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Documents.Persistence.DocumentsDbContext>();
        var v1 = await db.SharedCompanyDocumentVersions.SingleAsync(v => v.SharedCompanyDocumentId == doc.Id && v.VersionNumber == 1);
        var v2 = await db.SharedCompanyDocumentVersions.SingleAsync(v => v.SharedCompanyDocumentId == doc.Id && v.VersionNumber == 2);

        Assert.Equal("I confirm I have read the original policy.", v1.AcknowledgementStatement);
        Assert.Equal("I confirm I have read the original policy.", v2.AcknowledgementStatement);
    }

    [Fact]
    public async Task UploadVersion_Uses_Override_AcknowledgementStatement_Without_Mutating_Previous_Versions_Row()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(
            client, companyId, categoryId, title: "Remote Working Policy",
            requiresAcknowledgement: true, acknowledgementDueDate: new DateOnly(2027, 1, 1),
            acknowledgementStatement: "I confirm I have read the original policy.");

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/versions",
            BuildVersionUpload(requiresReacknowledgement: true, acknowledgementStatement: "HR-edited wording for the new version."));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Documents.Persistence.DocumentsDbContext>();
        var v1 = await db.SharedCompanyDocumentVersions.SingleAsync(v => v.SharedCompanyDocumentId == doc.Id && v.VersionNumber == 1);
        var v2 = await db.SharedCompanyDocumentVersions.SingleAsync(v => v.SharedCompanyDocumentId == doc.Id && v.VersionNumber == 2);

        Assert.Equal("HR-edited wording for the new version.", v2.AcknowledgementStatement);
        Assert.Equal("I confirm I have read the original policy.", v1.AcknowledgementStatement);
    }

    private sealed record VersionUploadPayload(Guid Id, Guid CompanyId, int VersionNumber, string FileName, string VersionNote);

    private static MultipartFormDataContent BuildVersionUpload(
        string versionNote = "Updated section 3", bool requiresReacknowledgement = false,
        string? acknowledgementStatement = null)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(versionNote), "VersionNote");
        form.Add(new StringContent(requiresReacknowledgement.ToString()), "RequiresReacknowledgement");
        if (acknowledgementStatement is not null)
            form.Add(new StringContent(acknowledgementStatement), "AcknowledgementStatement");

        var fileContent = new ByteArrayContent(PdfBytes());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        form.Add(fileContent, "File", "policy-v2.pdf");
        return form;
    }

    // ── DownloadSharedCompanyDocumentVersion ────────────────────────────────────

    [Fact]
    public async Task DownloadVersion_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response      = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/shared-documents/{Guid.NewGuid()}/versions/1/download");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DownloadVersion_Returns_NotFound_While_Version_Scan_Is_Pending()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId, allowAutoRedirect: false);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Documents.Persistence.DocumentsDbContext>();
            var version = await db.SharedCompanyDocumentVersions
                .SingleAsync(v => v.SharedCompanyDocumentId == doc!.Id && v.VersionNumber == 1);
            version.MarkScanning(DateTimeOffset.UtcNow); // UploadAsync helper marks Clean by default; revert.
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/versions/1/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadVersion_Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);

        using var managerClient = await ClientAs(companyId, managerId, allowAutoRedirect: false);
        var response = await managerClient.GetAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/versions/1/download");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DownloadVersion_Redirects_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId, allowAutoRedirect: false);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/versions/1/download");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task DownloadVersion_Returns_NotFound_For_Unknown_VersionNumber()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId, allowAutoRedirect: false);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/versions/99/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadVersion_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA     = Guid.NewGuid();
        var hrInB     = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        var (doc, _) = await UploadAsync(clientA, companyA, categoryInA);

        using var clientB = await ClientAs(companyB, hrInB, allowAutoRedirect: false);
        var response = await clientB.GetAsync(
            $"/api/companies/{companyB}/shared-documents/{doc!.Id}/versions/1/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── ArchiveSharedCompanyDocument ─────────────────────────────────────────────

    [Fact]
    public async Task Archive_Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);

        using var managerClient = await ClientAs(companyId, managerId);
        var response = await managerClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/archive",
            new { Reason = "No longer needed" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Archive_Succeeds_For_HrAdministrator_On_Draft_Document()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId, title: "Draft Policy");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/archive",
            new { Reason = "Draft was never needed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ArchivePayload>();
        Assert.Equal("Archived", payload!.Status);
        Assert.Equal(userId, payload.ArchivedBy);
        Assert.Equal("Draft was never needed", payload.ArchiveReason);
    }

    [Fact]
    public async Task Archive_Succeeds_For_HrAdministrator_On_Published_Document_And_Removes_From_PublishedList()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId, title: "Remote Working Policy");
        await client.PostAsync($"/api/companies/{companyId}/shared-documents/{doc!.Id}/publish", EmptyJson());

        var employeeId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);
        using var employeeClient = await ClientAs(companyId, employeeId);
        var beforeArchive = await employeeClient.GetFromJsonAsync<ListPayload>($"/api/companies/{companyId}/shared-documents/published");
        Assert.Contains(beforeArchive!.Items, i => i.Title == "Remote Working Policy");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/archive",
            new { Reason = "Superseded by a newer policy" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ArchivePayload>();
        Assert.Equal("Archived", payload!.Status);

        var afterArchive = await employeeClient.GetFromJsonAsync<ListPayload>($"/api/companies/{companyId}/shared-documents/published");
        Assert.DoesNotContain(afterArchive!.Items, i => i.Title == "Remote Working Policy");
    }

    [Fact]
    public async Task Archive_Returns_Conflict_When_Document_Already_Archived()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);

        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/archive", new { Reason = "First reason" });
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/archive", new { Reason = "Second reason" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Archive_Returns_NotFound_For_Unknown_Document()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{Guid.NewGuid()}/archive",
            new { Reason = "Reason" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Archive_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA     = Guid.NewGuid();
        var hrInB     = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        var (doc, _) = await UploadAsync(clientA, companyA, categoryInA);

        using var clientB = await ClientAs(companyB, hrInB);
        var response = await clientB.PostAsJsonAsync(
            $"/api/companies/{companyB}/shared-documents/{doc!.Id}/archive",
            new { Reason = "No longer needed" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Archive_Returns_Validation_Error_When_Reason_Missing()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/archive",
            new { Reason = "" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record ArchivePayload(
        Guid Id, Guid CompanyId, string Status, Guid ArchivedBy, DateTimeOffset ArchivedAt,
        string ArchiveReason, int AcknowledgementTasksCancelled);

    // ── ExpireSharedCompanyDocument ──────────────────────────────────────────────

    [Fact]
    public async Task Expire_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/api/companies/{Guid.NewGuid()}/shared-documents/{Guid.NewGuid()}/expire", EmptyJson());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Expire_Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        var hrUserId  = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");
        var (doc, _) = await UploadAsync(hrClient, companyId, categoryId);

        using var managerClient = await ClientAs(companyId, managerId);
        var response = await managerClient.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/expire", EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Expire_Succeeds_For_HrAdministrator_On_Published_Document()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId, title: "Aging Policy");
        await client.PostAsync($"/api/companies/{companyId}/shared-documents/{doc!.Id}/publish", EmptyJson());

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/expire", EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ExpirePayload>();
        Assert.Equal("Expired", payload!.Status);
        Assert.Equal(userId, payload.ExpiredBy);
    }

    [Fact]
    public async Task Expire_Returns_Conflict_When_Document_Already_Expired()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);

        await client.PostAsync($"/api/companies/{companyId}/shared-documents/{doc!.Id}/expire", EmptyJson());
        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/expire", EmptyJson());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Expire_Returns_Conflict_When_Document_Already_Archived()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");
        var (doc, _) = await UploadAsync(client, companyId, categoryId);

        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{doc!.Id}/archive", new { Reason = "Superseded" });
        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{doc.Id}/expire", EmptyJson());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Expire_Returns_NotFound_For_Unknown_Document()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents/{Guid.NewGuid()}/expire", EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Expire_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA    = Guid.NewGuid();
        var hrInB    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        var (doc, _) = await UploadAsync(clientA, companyA, categoryInA);

        using var clientB = await ClientAs(companyB, hrInB);
        var response = await clientB.PostAsync(
            $"/api/companies/{companyB}/shared-documents/{doc!.Id}/expire", EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record ExpirePayload(
        Guid Id, Guid CompanyId, string Status, Guid ExpiredBy, DateTimeOffset ExpiredAt,
        int ReviewTasksCancelled);

    // Seeds a Department directly via the Employees module's DbContext — there is no lighter-
    // weight way to get a real, existence-checkable department id into an integration test.
    private async Task<Guid> SeedDepartmentAsync(Guid companyId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Employees.Persistence.EmployeesDbContext>();
        var department = HR.Modules.Employees.Domain.Department.Create(Guid.NewGuid(), companyId, name, null, DateTimeOffset.UtcNow);
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        return department.Id;
    }

    // Seeds a real, Active Employee row with Id == employeeId — needed for tests whose caller
    // must be found by EmployeeAudienceReader.GetEligibleEmployeeIdsAsync (which queries real
    // Active Employees rows), unlike simpler per-employee audience checks elsewhere that only
    // need a role/claims assignment via TestRoleSeeder.
    private async Task CreateActiveEmployeeAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Employees.Persistence.EmployeesDbContext>();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);

        var now = DateTimeOffset.UtcNow;
        var employee = HR.Modules.Employees.Domain.Employee.Create(
            employeeId, companyId, "Ada", "Acknowledger", $"ada.{Guid.NewGuid():N}@example.com",
            new DateOnly(2026, 1, 1), hasSystemAccess: true, new DateOnly(1990, 1, 1), "British",
            "Prefer not to say", $"EMP-{Guid.NewGuid():N}", refData.EmploymentTypeId,
            refData.DepartmentId, refData.LocationId, refData.PositionProfileId, now);
        employee.Activate(now);

        db.Employees.Add(employee);
        await db.SaveChangesAsync();
    }

    // Directly flips a document to Published via the DbContext, bypassing the real Publish
    // endpoint's validation — used by tests that need a Published document in a state the real
    // endpoint wouldn't allow reaching (e.g. requires-acknowledgement without going through the
    // acknowledgement-settings endpoint first).
    private async Task PublishDirectlyAsync(Guid companyId, Guid documentId, bool requiresAcknowledgement = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Documents.Persistence.DocumentsDbContext>();
        var doc = await db.SharedCompanyDocuments.SingleAsync(d => d.Id == documentId && d.CompanyId == companyId);
        doc.Publish(Guid.NewGuid(), DateTimeOffset.UtcNow);
        if (requiresAcknowledgement)
        {
            doc.SetAcknowledgementSettings(true, DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)), null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        }
        await db.SaveChangesAsync();
    }

    private sealed record HrDetailPayload(
        IReadOnlyList<object> VersionHistory,
        string AudienceDescription);

    private sealed record ReviewFrequencyDetailPayload(
        Guid Id, string ReviewFrequency, int? CustomReviewFrequencyMonths,
        Guid? ReviewOwnerEmployeeId, string? ReviewOwnerName);

    private async Task<Guid> CreateCategoryAsync(HttpClient client, Guid companyId, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/document-categories", new { name });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CategoryPayload>();
        return payload!.Id;
    }

    private async Task<(DocumentPayload? Payload, HttpResponseMessage Response)> UploadAsync(
        HttpClient client, Guid companyId, Guid categoryId, string title = "Test Document",
        string? reviewFrequency = null, int? customReviewFrequencyMonths = null, DateOnly? reviewDate = null,
        Guid? reviewOwnerEmployeeId = null, bool requiresAcknowledgement = false, DateOnly? acknowledgementDueDate = null,
        string? acknowledgementStatement = null)
    {
        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents",
            BuildUpload(
                title, categoryId, reviewFrequency, customReviewFrequencyMonths, reviewDate, reviewOwnerEmployeeId,
                requiresAcknowledgement, acknowledgementDueDate, acknowledgementStatement));
        DocumentPayload? payload = null;
        if (response.IsSuccessStatusCode)
        {
            payload = await response.Content.ReadFromJsonAsync<DocumentPayload>();

            // Uploads are now scanned asynchronously via a Hangfire job (ScanUploadedFileJob),
            // which never actually runs inside these integration tests — simulate a completed
            // Clean scan directly so download/read tests exercised here don't need to know about
            // ScanStatusAccessGuard (that guard itself is covered by dedicated tests).
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Documents.Persistence.DocumentsDbContext>();
            var doc = await db.SharedCompanyDocuments.SingleAsync(d => d.Id == payload!.Id);
            doc.MarkScanClean(DateTimeOffset.UtcNow);
            var version = await db.SharedCompanyDocumentVersions
                .Where(v => v.SharedCompanyDocumentId == payload!.Id)
                .OrderByDescending(v => v.VersionNumber)
                .FirstAsync();
            version.MarkScanClean(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        return (payload, response);
    }

    private static MultipartFormDataContent BuildUpload(
        string title = "Test Document", Guid? categoryId = null,
        string? reviewFrequency = null, int? customReviewFrequencyMonths = null, DateOnly? reviewDate = null,
        Guid? reviewOwnerEmployeeId = null, bool requiresAcknowledgement = false, DateOnly? acknowledgementDueDate = null,
        string? acknowledgementStatement = null)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(title), "Title");
        form.Add(new StringContent((categoryId ?? Guid.NewGuid()).ToString()), "CategoryId");
        if (reviewFrequency is not null)
            form.Add(new StringContent(reviewFrequency), "ReviewFrequency");
        if (customReviewFrequencyMonths is not null)
            form.Add(new StringContent(customReviewFrequencyMonths.Value.ToString()), "CustomReviewFrequencyMonths");
        if (reviewDate is not null)
            form.Add(new StringContent(reviewDate.Value.ToString("yyyy-MM-dd")), "ReviewDate");
        if (reviewOwnerEmployeeId is not null)
            form.Add(new StringContent(reviewOwnerEmployeeId.Value.ToString()), "ReviewOwnerEmployeeId");
        if (requiresAcknowledgement)
            form.Add(new StringContent("true"), "RequiresAcknowledgement");
        if (acknowledgementDueDate is not null)
            form.Add(new StringContent(acknowledgementDueDate.Value.ToString("yyyy-MM-dd")), "AcknowledgementDueDate");
        if (acknowledgementStatement is not null)
            form.Add(new StringContent(acknowledgementStatement), "AcknowledgementStatement");

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

    private async Task<HttpClient> ClientAs(Guid companyId, Guid userId, bool allowAutoRedirect = true)
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
        });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        // Role-agnostic sync only — every caller of this helper already granted the specific
        // role(s) it wants to test beforehand via AssignRoleAsync. Hardcoding a role here (this
        // used to always grant SystemRoles.Manager) additionally granted it to every caller
        // regardless of intent, which used to be harmless only because tenant resolution didn't
        // actually key off UserProfile.CompanyId yet — now that it does, an unconditional extra
        // role grant here changes real authorization outcomes.
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    // Passing null as HttpContent to PostAsync omits the Content-Type header entirely, which
    // FastEndpoints rejects with 415 Unsupported Media Type once past authorization — an empty
    // JSON body is the minimal content that satisfies model binding for these no-payload actions.
    private static StringContent EmptyJson() =>
        new("{}", Encoding.UTF8, "application/json");

    private sealed record CategoryPayload(Guid Id, Guid CompanyId, string Name, bool IsActive);
    private sealed record DocumentPayload(Guid Id, string Title, string Status, int VersionNumber);
    private sealed record ListPayload(IReadOnlyList<ListItem> Items);
    private sealed record ListItem(
        Guid Id, string Title, string CategoryName, string Status, string UpdatedByName,
        string ReviewFrequency, Guid? ReviewOwnerEmployeeId, string? ReviewOwnerName);
}
