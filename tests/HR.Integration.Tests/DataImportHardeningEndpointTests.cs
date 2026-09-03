using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// TEST-007 end-to-end hardening for the employee data-import pipeline: duplicate rows within a
/// file, unsupported file types, and partial-failure confirm bookkeeping that must reach the
/// persisted session + error report. Cross-tenant isolation and double-confirm rejection are
/// covered in ConfirmImportSessionEndpointTests / ValidateImportSessionEndpointTests /
/// ExportImportErrorsEndpointTests.
/// </summary>
[Collection("Integration")]
public class DataImportHardeningEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ImportAdmin = Guid.Parse("59000000-0000-0000-0000-0000000000AA");

    public DataImportHardeningEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Upload_Rejects_Unsupported_File_Type()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var content = new MultipartFormDataContent();
        content.Add(new StringContent("Employee"), "EntityType");
        var fileContent = new ByteArrayContent("name,email\nJohn,john@example.com"u8.ToArray());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
        content.Add(fileContent, "File", "employees.csv");

        var response = await client.PostAsync($"/api/companies/{companyId}/data-import/sessions", content);

        // The upload handler rejects a disallowed file type as a domain validation failure, which
        // the endpoint maps to 422 UnprocessableEntity (not a 400 model-binding failure).
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_Work_Email_Rows_Both_Fail_Validation_And_Appear_In_The_Error_Report()
    {
        var (client, companyId) = await ManualModeAdminClientAsync();
        using var _ = client;
        await EnsureDefaultLeavePolicyAsync(client, companyId);

        // Row 2 is a unique, fully valid employee. Rows 3 and 4 share a work email, so per
        // EmployeeStagingRowValidator both are flagged (duplicate-within-file is symmetric).
        const string csv =
            "First Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile,Salary Amount\n" +
            "John,Doe,john.doe@example.com,2026-01-01,EMP001,1990-01-01,British,Male,Sales,London,Permanent,Software Developer,50000\n" +
            "Jane,Roe,dup@example.com,2026-01-02,EMP002,1991-02-02,British,Female,Sales,London,Permanent,Software Developer,50000\n" +
            "Jack,Poe,dup@example.com,2026-01-03,EMP003,1992-03-03,British,Male,Sales,London,Permanent,Software Developer,50000\n";

        var sessionId = await UploadAsync(client, companyId, csv);

        var validate = await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson());
        validate.EnsureSuccessStatusCode();
        var payload = await validate.Content.ReadFromJsonAsync<ValidatePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Validated", payload!.Status);
        Assert.Equal(1, payload.SuccessfulRows);
        Assert.Equal(2, payload.FailedRows);

        var export = await client.GetAsync(ExportUrl(companyId, sessionId));
        export.EnsureSuccessStatusCode();
        var body = await export.Content.ReadAsStringAsync();
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(lines, l => l.StartsWith("3,Error,") && l.Contains("Duplicate work email"));
        Assert.Contains(lines, l => l.StartsWith("4,Error,") && l.Contains("Duplicate work email"));
    }

    [Fact]
    public async Task Confirm_Of_A_File_With_Invalid_Rows_Creates_Only_Valid_Rows_And_Persists_Exact_Counts()
    {
        var (client, companyId) = await ManualModeAdminClientAsync();
        using var _ = client;
        await EnsureDefaultLeavePolicyAsync(client, companyId);

        // Row 2 valid; row 3 missing Last Name -> invalid at validate. Confirm must import row 2
        // only, and the session must land on CompletedWithErrors with FailedCount == 1 (the
        // already-invalid row is carried into the confirm bookkeeping, not silently dropped).
        const string csv =
            "First Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile,Salary Amount\n" +
            "John,Doe,john.doe@example.com,2026-01-01,EMP001,1990-01-01,British,Male,Sales,London,Permanent,Software Developer,50000\n" +
            "Jane,,jane.doe@example.com,2026-01-02,EMP002,1991-02-02,British,Female,Sales,London,Permanent,Software Developer,50000\n";

        var sessionId = await UploadAsync(client, companyId, csv);
        (await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson())).EnsureSuccessStatusCode();

        var confirm = await client.PostAsync(ConfirmUrl(companyId, sessionId), EmptyJson());
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var payload = await confirm.Content.ReadFromJsonAsync<ConfirmPayload>();
        Assert.NotNull(payload);
        Assert.Equal("CompletedWithErrors", payload!.Status);
        Assert.Equal(1, payload.CreatedCount);
        Assert.Equal(1, payload.FailedCount);

        var employees = await client.GetFromJsonAsync<ListEmployeesPayload>(
            $"/api/companies/{companyId}/employees?pageSize=50");
        Assert.NotNull(employees);
        Assert.Contains(employees!.Items, e => e.WorkEmail == "john.doe@example.com");
        Assert.DoesNotContain(employees.Items, e => e.WorkEmail == "jane.doe@example.com");

        // A second confirm is rejected - no duplicate employee records.
        var second = await client.PostAsync(ConfirmUrl(companyId, sessionId), EmptyJson());
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, ImportAdmin, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private async Task<(HttpClient Client, Guid CompanyId)> ManualModeAdminClientAsync()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"DataImport Hardening Co {Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, ImportAdmin, SystemRoles.HrAdministrator, companyId);
        await SetEmployeeNumberModeAsync(client, companyId, "Manual");
        return (client, companyId);
    }

    private static async Task SetEmployeeNumberModeAsync(HttpClient client, Guid companyId, string mode)
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
            employeeNumberPrefix = (string?)null,
            nextEmployeeNumber = 1,
            employeeNumberMinimumLength = 1
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task EnsureDefaultLeavePolicyAsync(HttpClient client, Guid companyId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Default Policy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        response.EnsureSuccessStatusCode();
    }

    private static string ValidateUrl(Guid c, Guid s) => $"/api/companies/{c}/data-import/sessions/{s}/validate";
    private static string ConfirmUrl(Guid c, Guid s) => $"/api/companies/{c}/data-import/sessions/{s}/confirm";
    private static string ExportUrl(Guid c, Guid s) => $"/api/companies/{c}/data-import/sessions/{s}/errors/export";

    private static async Task<Guid> UploadAsync(HttpClient client, Guid companyId, string csvContent)
    {
        var response = await client.PostAsync(
            $"/api/companies/{companyId}/data-import/sessions", BuildCsvUpload(csvContent));
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

    private static StringContent EmptyJson() => new("{}", Encoding.UTF8, "application/json");

    private sealed record UploadPayload(
        Guid Id, Guid CompanyId, string EntityType, string FileName, string Status, int TotalRows, DateTimeOffset CreatedAt);

    private sealed record ValidatePayload(Guid Id, string Status, int TotalRows, int SuccessfulRows, int FailedRows);

    private sealed record ConfirmPayload(Guid ImportSessionId, string Status, int CreatedCount, int FailedCount);

    private sealed record EmployeeListItemPayload(Guid Id, string WorkEmail);

    private sealed record ListEmployeesPayload(
        List<EmployeeListItemPayload> Items, int TotalCount, int PageNumber, int PageSize, int TotalPages);
}
