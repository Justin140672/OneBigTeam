using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetImportSessionEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ImportAdmin = Guid.Parse("63000000-0000-0000-0000-000000000001");

    public GetImportSessionEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Ok_With_Correct_Detail_After_Uploading_A_Session()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var sessionId = await UploadAsync(client, companyId, ValidCsv());

        var response = await client.GetAsync(SessionUrl(companyId, sessionId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SessionDetailPayload>();
        Assert.NotNull(payload);
        Assert.Equal(sessionId, payload!.Id);
        Assert.Equal("Employee", payload.EntityType);
        Assert.Equal("employees.xlsx", payload.FileName);
        Assert.Equal("Pending", payload.Status);
        Assert.Equal(2, payload.TotalRows);
        Assert.Equal(0, payload.SuccessfulRows);
        Assert.Equal(0, payload.FailedRows);
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(SessionUrl(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_When_Session_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.GetAsync(SessionUrl(companyId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // These two tests preserve coverage that used to live in the E2E suite's (now-removed, see
    // item 47 — Import History screen deleted) ImportHistoryTests: that a completed import
    // session's Status/TotalRows/SuccessfulRows/FailedRows are correctly reflected via
    // GetImportSession once the full upload -> validate -> confirm flow has actually run, not
    // just right after upload (Pending, all counts zero — the only state the tests above cover).
    // The Import History UI screen is gone, but ListImportSessions/GetImportSession themselves
    // remain in place and still need to report accurate post-confirm state for any other caller.
    [Fact]
    public async Task Returns_Imported_Status_And_Correct_Row_Counts_After_A_Fully_Successful_Confirm()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());
        var companyId = await CreateCompanyAsync(client);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, ImportAdmin, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, ImportAdmin, SystemRoles.CompanyAdministrator, companyId);

        await EnsureDefaultLeavePolicyAsync(client, companyId);
        await SetEmployeeNumberModeAsync(client, companyId, "Manual");

        var sessionId = await UploadAsync(client, companyId, FullyValidCsv());
        (await client.PostAsync(
            $"/api/companies/{companyId}/data-import/sessions/{sessionId}/validate", EmptyJson()))
            .EnsureSuccessStatusCode();
        (await client.PostAsync(
            $"/api/companies/{companyId}/data-import/sessions/{sessionId}/confirm", EmptyJson()))
            .EnsureSuccessStatusCode();

        var response = await client.GetAsync(SessionUrl(companyId, sessionId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SessionDetailPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Imported", payload!.Status);
        Assert.Equal(2, payload.TotalRows);
        Assert.Equal(2, payload.SuccessfulRows);
        Assert.Equal(0, payload.FailedRows);

        // Also preserved from the removed E2E test: the session must appear correctly in the list
        // view too, not only its own detail endpoint.
        var listResponse = await client.GetAsync($"/api/companies/{companyId}/data-import/sessions");
        listResponse.EnsureSuccessStatusCode();
        var sessions = await listResponse.Content.ReadFromJsonAsync<List<SessionSummaryPayload>>();
        Assert.NotNull(sessions);
        var summary = Assert.Single(sessions!, s => s.Id == sessionId);
        Assert.Equal("Imported", summary.Status);
        Assert.Equal(2, summary.TotalRows);
        Assert.Equal(2, summary.SuccessfulRows);
        Assert.Equal(0, summary.FailedRows);
    }

    [Fact]
    public async Task Returns_CompletedWithErrors_Status_And_Correct_Row_Counts_After_A_Partially_Failed_Confirm()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());
        var companyId = await CreateCompanyAsync(client);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, ImportAdmin, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, ImportAdmin, SystemRoles.CompanyAdministrator, companyId);

        await EnsureDefaultLeavePolicyAsync(client, companyId);
        await SetEmployeeNumberModeAsync(client, companyId, "Manual");

        // Second row is missing the required Last Name — one valid row, one row that fails
        // EmployeeStagingRowValidator's RequiredFields check.
        const string csv =
            "First Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile,Salary Amount\n" +
            "Valid,Employee,valid.employee@example.com,2026-01-01,EMP-VALID,1990-01-01,British,Male,Sales,London,Permanent,Software Developer,50000\n" +
            "Invalid,,invalid.employee@example.com,2026-01-02,EMP-INVALID,1990-01-01,British,Male,Sales,London,Permanent,Software Developer,50000\n";

        var sessionId = await UploadAsync(client, companyId, csv);
        (await client.PostAsync(
            $"/api/companies/{companyId}/data-import/sessions/{sessionId}/validate", EmptyJson()))
            .EnsureSuccessStatusCode();
        (await client.PostAsync(
            $"/api/companies/{companyId}/data-import/sessions/{sessionId}/confirm", EmptyJson()))
            .EnsureSuccessStatusCode();

        var response = await client.GetAsync(SessionUrl(companyId, sessionId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SessionDetailPayload>();
        Assert.NotNull(payload);
        Assert.Equal("CompletedWithErrors", payload!.Status);
        Assert.Equal(2, payload.TotalRows);
        Assert.Equal(1, payload.SuccessfulRows);
        Assert.Equal(1, payload.FailedRows);
    }

    [Fact]
    public async Task Returns_NotFound_When_Session_Belongs_To_A_Different_Company()
    {
        var ownerCompanyId = Guid.NewGuid();
        var callerCompanyId = Guid.NewGuid();

        using var ownerClient = await AdminClient(ownerCompanyId);
        var sessionId = await UploadAsync(ownerClient, ownerCompanyId, ValidCsv());

        using var callerClient = await AdminClient(callerCompanyId);

        var response = await callerClient.GetAsync(SessionUrl(callerCompanyId, sessionId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, ImportAdmin, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    // POST /api/companies (CreateCompany) was removed in 78a43344; this now provisions the
    // company directly via CompaniesDbContext — same as ConfirmImportSessionEndpointTests'
    // identical helper.
    private async Task<Guid> CreateCompanyAsync(HttpClient client)
    {
        _ = client;
        return await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Import GetSession Test Co {Guid.NewGuid():N}");
    }

    /// <summary>
    /// DefaultLeavePolicyId is mandatory on PositionProfile, so ImportLookupResolver's
    /// auto-create-position-profile path can only succeed once the company already has a default
    /// leave policy (the first policy created for a company is automatically its default).
    /// </summary>
    private static async Task EnsureDefaultLeavePolicyAsync(HttpClient client, Guid companyId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Default Policy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        response.EnsureSuccessStatusCode();
    }

    private static async Task SetEmployeeNumberModeAsync(
        HttpClient client, Guid companyId, string mode, string? prefix = null, int nextEmployeeNumber = 1, int minimumLength = 1)
    {
        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", new
        {
            id = companyId,
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 1,
            defaultHolidayAllowance = 25,
            probationMonths = 6,
            employeeNumberMode = mode,
            employeeNumberPrefix = prefix,
            nextEmployeeNumber,
            employeeNumberMinimumLength = minimumLength
        });
        response.EnsureSuccessStatusCode();
    }

    private static StringContent EmptyJson() => new("{}", Encoding.UTF8, "application/json");

    private static string SessionUrl(Guid companyId, Guid sessionId) =>
        $"/api/companies/{companyId}/data-import/sessions/{sessionId}";

    private static string ValidCsv() =>
        "First Name,Last Name,Work Email,Start Date,Employee Number\n" +
        "John,Doe,john.doe@example.com,2026-01-01,EMP001\n" +
        "Jane,Doe,jane.doe@example.com,2026-01-02,EMP002\n";

    // Unlike ValidCsv() above (upload/detail-only tests, never validated/confirmed), this includes
    // every field EmployeeStagingRowValidator.RequiredFields/RequiredLookupFields needs so rows
    // actually pass staging validation and can be confirmed — same shape as
    // ConfirmImportSessionEndpointTests' own ValidCsv().
    private static string FullyValidCsv() =>
        "First Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile,Salary Amount\n" +
        "John,Doe,john.doe@example.com,2026-01-01,EMP001,1990-01-01,British,Male,Sales,London,Permanent,Software Developer,50000\n" +
        "Jane,Doe,jane.doe@example.com,2026-01-02,EMP002,1991-02-02,British,Female,Sales,London,Permanent,Software Developer,50000\n";

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

    private sealed record SessionDetailPayload(
        Guid Id, string EntityType, string FileName, string Status, int TotalRows, int ProcessedRows,
        int SuccessfulRows, int FailedRows, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
        string? ErrorSummary, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    private sealed record SessionSummaryPayload(
        Guid Id, string FileName, string Status, int TotalRows, int SuccessfulRows, int FailedRows,
        DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
}
