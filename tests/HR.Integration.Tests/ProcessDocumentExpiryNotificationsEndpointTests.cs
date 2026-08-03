using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ProcessDocumentExpiryNotificationsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ExpiryAdmin = Guid.Parse("ee000002-0000-0000-0000-000000000001");
    private static readonly DateOnly Today    = DateOnly.FromDateTime(DateTime.UtcNow);

    public ProcessDocumentExpiryNotificationsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, ExpiryAdmin, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ExpiryAdmin, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/documents/expiry-notifications", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_Without_Employee_Manage_Role()
    {
        var companyId    = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/documents/expiry-notifications", new { });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Zero_Counts_When_No_Documents_Need_Notification()
    {
        var (companyId, _, client) = await SetupAsync();

        var response = await client.PostAsJsonAsync(NotifUrl(companyId), new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<NotifPayload>();
        Assert.Equal(0, payload!.ExpiringSoonCount);
        Assert.Equal(0, payload.ExpiredCount);
    }

    [Fact]
    public async Task Returns_Zero_Counts_When_No_Documents_Have_ExpiryDate()
    {
        var (companyId, docTypeId, client) = await SetupAsync();
        await UploadDocAsync(client, companyId, docTypeId, Guid.NewGuid(), expiryDate: null);

        var response = await client.PostAsJsonAsync(NotifUrl(companyId), new { });
        var payload  = await response.Content.ReadFromJsonAsync<NotifPayload>();

        Assert.Equal(0, payload!.ExpiringSoonCount);
        Assert.Equal(0, payload.ExpiredCount);
    }

    [Fact]
    public async Task Returns_Correct_Counts_For_Expiring_And_Expired_Documents()
    {
        var (companyId, docTypeId, client) = await SetupAsync();

        await UploadDocAsync(client, companyId, docTypeId, Guid.NewGuid(), Today.AddDays(10));
        await UploadDocAsync(client, companyId, docTypeId, Guid.NewGuid(), Today.AddDays(-5));

        var response = await client.PostAsJsonAsync(NotifUrl(companyId), new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<NotifPayload>();
        Assert.Equal(1, payload!.ExpiringSoonCount);
        Assert.Equal(1, payload.ExpiredCount);
    }

    [Fact]
    public async Task Ignores_Documents_With_ExpiryDate_Beyond_Threshold()
    {
        var (companyId, docTypeId, client) = await SetupAsync();
        await UploadDocAsync(client, companyId, docTypeId, Guid.NewGuid(), Today.AddDays(31));

        var response = await client.PostAsJsonAsync(NotifUrl(companyId), new { });
        var payload  = await response.Content.ReadFromJsonAsync<NotifPayload>();

        Assert.Equal(0, payload!.ExpiringSoonCount);
        Assert.Equal(0, payload.ExpiredCount);
    }

    [Fact]
    public async Task Second_Call_Returns_Zero_Counts()
    {
        var (companyId, docTypeId, client) = await SetupAsync();

        await UploadDocAsync(client, companyId, docTypeId, Guid.NewGuid(), Today.AddDays(10));
        await UploadDocAsync(client, companyId, docTypeId, Guid.NewGuid(), Today.AddDays(-3));

        var url = NotifUrl(companyId);
        await client.PostAsJsonAsync(url, new { });  // first call — processes both

        var response2 = await client.PostAsJsonAsync(url, new { });
        var payload2  = await response2.Content.ReadFromJsonAsync<NotifPayload>();
        Assert.Equal(0, payload2!.ExpiringSoonCount);
        Assert.Equal(0, payload2.ExpiredCount);
    }

    [Fact]
    public async Task Processes_Multiple_ExpiringSoon_And_Expired_Documents()
    {
        var (companyId, docTypeId, client) = await SetupAsync();

        await UploadDocAsync(client, companyId, docTypeId, Guid.NewGuid(), Today.AddDays(5));
        await UploadDocAsync(client, companyId, docTypeId, Guid.NewGuid(), Today.AddDays(25));
        await UploadDocAsync(client, companyId, docTypeId, Guid.NewGuid(), Today.AddDays(-1));
        await UploadDocAsync(client, companyId, docTypeId, Guid.NewGuid(), Today.AddDays(-10));

        var response = await client.PostAsJsonAsync(NotifUrl(companyId), new { });
        var payload  = await response.Content.ReadFromJsonAsync<NotifPayload>();

        Assert.Equal(2, payload!.ExpiringSoonCount);
        Assert.Equal(2, payload.ExpiredCount);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private async Task<(Guid CompanyId, Guid DocTypeId, HttpClient Client)> SetupAsync()
    {
        var companyId = Guid.NewGuid();
        var client    = AdminClient(companyId);

        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-types",
            new { name = "Contract", allowEmployeeUpload = false });
        resp.EnsureSuccessStatusCode();

        var docType = await resp.Content.ReadFromJsonAsync<DocTypePayload>();
        return (companyId, docType!.Id, client);
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ExpiryAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private static async Task UploadDocAsync(
        HttpClient client, Guid companyId, Guid docTypeId, Guid employeeId, DateOnly? expiryDate)
    {
        var content  = BuildPdfUpload(docTypeId, expiryDate);
        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/documents", content);
        response.EnsureSuccessStatusCode();
    }

    private static MultipartFormDataContent BuildPdfUpload(Guid docTypeId, DateOnly? expiryDate = null)
    {
        var pdfBytes = new byte[1024];
        pdfBytes[0] = 0x25; pdfBytes[1] = 0x50; pdfBytes[2] = 0x44; pdfBytes[3] = 0x46; // %PDF

        var content = new MultipartFormDataContent();
        content.Add(new StringContent("Test Document"),         "Title");
        content.Add(new StringContent(docTypeId.ToString()),    "DocumentTypeId");

        if (expiryDate.HasValue)
            content.Add(new StringContent(expiryDate.Value.ToString("yyyy-MM-dd")), "ExpiryDate");

        var file = new ByteArrayContent(pdfBytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(file, "File", "test.pdf");

        return content;
    }

    private static string NotifUrl(Guid companyId) =>
        $"/api/companies/{companyId}/documents/expiry-notifications";

    private sealed record DocTypePayload(Guid Id);
    private sealed record NotifPayload(int ExpiringSoonCount, int ExpiredCount);
}
