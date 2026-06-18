using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetExpiringDocumentsEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ExpiryAdmin = Guid.Parse("ee000001-0000-0000-0000-000000000001");
    private static readonly DateOnly Today    = DateOnly.FromDateTime(DateTime.UtcNow);

    public GetExpiringDocumentsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, ExpiryAdmin, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/documents/expiring");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Empty_When_No_Documents_Have_ExpiryDate()
    {
        var (companyId, docTypeId, client) = await SetupAsync();
        await UploadDocAsync(client, companyId, docTypeId, Guid.NewGuid(), expiryDate: null);

        var response = await client.GetAsync(ExpiringUrl(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ExpiringPayload>();
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Returns_Document_With_ExpiringSoon_Status()
    {
        var (companyId, docTypeId, client) = await SetupAsync();
        var employeeId = Guid.NewGuid();
        var expiryDate = Today.AddDays(10);

        var uploadResponse = await UploadDocAsync(client, companyId, docTypeId, employeeId, expiryDate);
        var uploaded       = await uploadResponse.Content.ReadFromJsonAsync<UploadPayload>();

        var response = await client.GetAsync(ExpiringUrl(companyId));
        var payload  = await response.Content.ReadFromJsonAsync<ExpiringPayload>();

        var item = Assert.Single(payload!.Items);
        Assert.Equal(uploaded!.EmployeeDocumentId, item.EmployeeDocumentId);
        Assert.Equal(employeeId,                   item.EmployeeId);
        Assert.Equal(expiryDate,                   item.ExpiryDate);
        Assert.Equal("ExpiringSoon",               item.ExpiryStatus);
    }

    [Fact]
    public async Task Returns_Document_With_Expired_Status()
    {
        var (companyId, docTypeId, client) = await SetupAsync();
        var employeeId = Guid.NewGuid();
        var expiryDate = Today.AddDays(-3);

        var uploadResponse = await UploadDocAsync(client, companyId, docTypeId, employeeId, expiryDate);
        var uploaded       = await uploadResponse.Content.ReadFromJsonAsync<UploadPayload>();

        var response = await client.GetAsync(ExpiringUrl(companyId));
        var payload  = await response.Content.ReadFromJsonAsync<ExpiringPayload>();

        var item = Assert.Single(payload!.Items);
        Assert.Equal(uploaded!.EmployeeDocumentId, item.EmployeeDocumentId);
        Assert.Equal("Expired",                    item.ExpiryStatus);
    }

    [Fact]
    public async Task Does_Not_Return_Document_With_ExpiryDate_Beyond_Threshold()
    {
        var (companyId, docTypeId, client) = await SetupAsync();
        await UploadDocAsync(client, companyId, docTypeId, Guid.NewGuid(), Today.AddDays(35));

        var response = await client.GetAsync(ExpiringUrl(companyId));
        var payload  = await response.Content.ReadFromJsonAsync<ExpiringPayload>();

        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Returns_Mix_Of_ExpiringSoon_And_Expired_Ordered_By_ExpiryDate()
    {
        var (companyId, docTypeId, client) = await SetupAsync();

        await UploadDocAsync(client, companyId, docTypeId, Guid.NewGuid(), Today.AddDays(20));
        await UploadDocAsync(client, companyId, docTypeId, Guid.NewGuid(), Today.AddDays(-2));
        await UploadDocAsync(client, companyId, docTypeId, Guid.NewGuid(), Today.AddDays(5));

        var response = await client.GetAsync(ExpiringUrl(companyId));
        var payload  = await response.Content.ReadFromJsonAsync<ExpiringPayload>();

        Assert.Equal(3, payload!.Items.Count);
        Assert.Equal(2, payload.Items.Count(i => i.ExpiryStatus == "ExpiringSoon"));
        Assert.Equal(1, payload.Items.Count(i => i.ExpiryStatus == "Expired"));
        // Ordered ascending by ExpiryDate: expired first, then soonest
        Assert.True(payload.Items[0].ExpiryDate <= payload.Items[1].ExpiryDate);
        Assert.True(payload.Items[1].ExpiryDate <= payload.Items[2].ExpiryDate);
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

    private static async Task<HttpResponseMessage> UploadDocAsync(
        HttpClient client, Guid companyId, Guid docTypeId, Guid employeeId, DateOnly? expiryDate)
    {
        var content  = BuildPdfUpload(docTypeId, expiryDate);
        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/documents", content);
        response.EnsureSuccessStatusCode();
        return response;
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

    private static string ExpiringUrl(Guid companyId) =>
        $"/api/companies/{companyId}/documents/expiring";

    private sealed record DocTypePayload(Guid Id);
    private sealed record UploadPayload(Guid EmployeeDocumentId, Guid EmployeeId);
    private sealed record ExpiringPayload(IReadOnlyList<ExpiringDocItem> Items);
    private sealed record ExpiringDocItem(
        Guid     EmployeeDocumentId,
        Guid     EmployeeId,
        string   Title,
        string   DocumentTypeName,
        DateOnly ExpiryDate,
        string   ExpiryStatus);
}
