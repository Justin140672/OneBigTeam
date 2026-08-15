using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListImportSessionsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ImportAdmin = Guid.Parse("62000000-0000-0000-0000-000000000001");

    public ListImportSessionsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Ok_Listing_Uploaded_Sessions()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var sessionId = await UploadAsync(client, companyId, ValidCsv());

        var response = await client.GetAsync(ListUrl(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<SessionSummaryPayload>>();
        Assert.NotNull(payload);
        Assert.Contains(payload!, s => s.Id == sessionId && s.Status == "Pending" && s.FileName == "employees.xlsx");
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(ListUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Ok_With_Empty_List_For_Company_With_No_Sessions()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.GetAsync(ListUrl(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<SessionSummaryPayload>>();
        Assert.NotNull(payload);
        Assert.Empty(payload!);
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, ImportAdmin, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private static string ListUrl(Guid companyId) =>
        $"/api/companies/{companyId}/data-import/sessions";

    private static string ValidCsv() =>
        "First Name,Last Name,Work Email,Start Date,Employee Number\n" +
        "John,Doe,john.doe@example.com,2026-01-01,EMP001\n";

    private static async Task<Guid> UploadAsync(HttpClient client, Guid companyId, string csvContent)
    {
        var response = await client.PostAsync(
            $"/api/companies/{companyId}/data-import/sessions",
            BuildCsvUpload(csvContent));

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<UploadPayload>();
        Assert.NotNull(payload);
        return payload!.Id;
    }

    private static MultipartFormDataContent BuildCsvUpload(string csvContent)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("Employee"), "EntityType");

        var fileContent = new ByteArrayContent(BuildXlsxBytes(csvContent));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "File", "employees.xlsx");

        return content;
    }

    // Builds a minimal XLSX workbook (via ClosedXML) from comma-delimited "csv-shaped" header/data
    // lines, so existing test fixtures (written as csv-style strings for readability) can still be
    // uploaded against the now xlsx-only import endpoint.
    private static byte[] BuildXlsxBytes(string csvShapedContent)
    {
        var lines = csvShapedContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToList();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Sheet1");

        for (var row = 0; row < lines.Count; row++)
        {
            var cells = lines[row].Split(',');
            for (var col = 0; col < cells.Length; col++)
                worksheet.Cell(row + 1, col + 1).Value = cells[col];
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private sealed record UploadPayload(
        Guid Id, Guid CompanyId, string EntityType, string FileName, string Status,
        int TotalRows, DateTimeOffset CreatedAt);

    private sealed record SessionSummaryPayload(
        Guid Id, string FileName, string Status, int TotalRows, int SuccessfulRows, int FailedRows,
        DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
}
