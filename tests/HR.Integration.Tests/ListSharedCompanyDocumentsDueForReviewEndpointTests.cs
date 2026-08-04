using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies ListSharedCompanyDocumentsDueForReview end-to-end: only documents whose ReviewDate
/// is within the next 7 days (overdue, due today, or due this week) come back, Archived documents
/// are always excluded regardless of ReviewDate, and results are scoped to the company in the route.
/// </summary>
[Collection("Integration")]
public class ListSharedCompanyDocumentsDueForReviewEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    public ListSharedCompanyDocumentsDueForReviewEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.GetAsync(DueForReviewUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Overdue_And_DueToday_Documents_But_Excludes_Future_And_Archived()
    {
        var companyId  = Guid.NewGuid();
        var userId     = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");

        var (overdue, _)   = await UploadAsync(client, companyId, categoryId, "Overdue Policy", Today.AddDays(-5));
        var (dueToday, _)  = await UploadAsync(client, companyId, categoryId, "Due Today Policy", Today);
        await UploadAsync(client, companyId, categoryId, "Future Policy", Today.AddDays(10));
        var (archived, _)  = await UploadAsync(client, companyId, categoryId, "Archived Overdue Policy", Today.AddDays(-3));

        var archiveResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/shared-documents/{archived!.Id}/archive",
            new { Reason = "No longer needed" });
        Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);

        var response = await client.GetAsync(DueForReviewUrl(companyId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<DueForReviewPayload>();
        Assert.Equal(2, payload!.Items.Count);
        Assert.Contains(payload.Items, i => i.Id == overdue!.Id && i.Title == "Overdue Policy");
        Assert.Contains(payload.Items, i => i.Id == dueToday!.Id && i.Title == "Due Today Policy");
        Assert.DoesNotContain(payload.Items, i => i.Title == "Future Policy");
        Assert.DoesNotContain(payload.Items, i => i.Title == "Archived Overdue Policy");
    }

    [Fact]
    public async Task Returns_IsOverdue_True_For_Overdue_Document_And_False_For_Due_This_Week_Document()
    {
        var companyId  = Guid.NewGuid();
        var userId     = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");

        var (overdue, _)      = await UploadAsync(client, companyId, categoryId, "Overdue Policy", Today.AddDays(-2));
        var (dueThisWeek, _)  = await UploadAsync(client, companyId, categoryId, "Due This Week Policy", Today.AddDays(4));

        var response = await client.GetAsync(DueForReviewUrl(companyId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<DueForReviewPayload>();
        Assert.Equal(2, payload!.Items.Count);

        var overdueItem     = payload.Items.Single(i => i.Id == overdue!.Id);
        var dueThisWeekItem = payload.Items.Single(i => i.Id == dueThisWeek!.Id);
        Assert.True(overdueItem.IsOverdue);
        Assert.False(dueThisWeekItem.IsOverdue);
    }

    [Fact]
    public async Task Includes_Document_Due_Exactly_Seven_Days_Out_And_Excludes_Document_Due_Eight_Days_Out()
    {
        var companyId  = Guid.NewGuid();
        var userId     = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");

        var (withinWindow, _) = await UploadAsync(client, companyId, categoryId, "Exactly Seven Days Policy", Today.AddDays(7));
        await UploadAsync(client, companyId, categoryId, "Eight Days Out Policy", Today.AddDays(8));

        var response = await client.GetAsync(DueForReviewUrl(companyId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<DueForReviewPayload>();
        Assert.Single(payload!.Items);
        Assert.Contains(payload.Items, i => i.Id == withinWindow!.Id && i.Title == "Exactly Seven Days Policy");
        Assert.DoesNotContain(payload.Items, i => i.Title == "Eight Days Out Policy");
        Assert.False(payload.Items.Single().IsOverdue);
    }

    [Fact]
    public async Task Excludes_Documents_From_Other_Companies()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA    = Guid.NewGuid();
        var hrInB    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");
        await UploadAsync(clientA, companyA, categoryInA, "Company A Only Overdue Policy", Today.AddDays(-1));

        using var clientB = await ClientAs(companyB, hrInB);
        var response = await clientB.GetAsync(DueForReviewUrl(companyB));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DueForReviewPayload>();
        Assert.DoesNotContain(payload!.Items, i => i.Title == "Company A Only Overdue Policy");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static string DueForReviewUrl(Guid companyId) =>
        $"/api/companies/{companyId}/shared-documents/due-for-review";

    private async Task<HttpClient> ClientAs(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private async Task<Guid> CreateCategoryAsync(HttpClient client, Guid companyId, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/document-categories", new { name });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CategoryPayload>();
        return payload!.Id;
    }

    private static async Task<(DocumentPayload? Payload, HttpResponseMessage Response)> UploadAsync(
        HttpClient client, Guid companyId, Guid categoryId, string title, DateOnly reviewDate)
    {
        var response = await client.PostAsync(
            $"/api/companies/{companyId}/shared-documents",
            BuildUpload(title, categoryId, reviewDate));
        DocumentPayload? payload = null;
        if (response.IsSuccessStatusCode)
            payload = await response.Content.ReadFromJsonAsync<DocumentPayload>();
        return (payload, response);
    }

    private static MultipartFormDataContent BuildUpload(string title, Guid categoryId, DateOnly reviewDate)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(title), "Title");
        form.Add(new StringContent(categoryId.ToString()), "CategoryId");
        form.Add(new StringContent(reviewDate.ToString("yyyy-MM-dd")), "ReviewDate");

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

    private sealed record CategoryPayload(Guid Id, Guid CompanyId, string Name, bool IsActive);
    private sealed record DocumentPayload(Guid Id, string Title, string Status, int VersionNumber);
    private sealed record DueForReviewPayload(IReadOnlyList<DueForReviewItem> Items);
    private sealed record DueForReviewItem(
        Guid Id, string Title, string CategoryName, string Status, DateOnly? ReviewDate,
        string ReviewFrequency, Guid? ReviewOwnerEmployeeId, string? ReviewOwnerName, string UpdatedByName,
        bool IsOverdue);
}
