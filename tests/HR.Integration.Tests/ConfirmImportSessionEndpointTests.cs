using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ConfirmImportSessionEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ImportAdmin = Guid.Parse("59000000-0000-0000-0000-000000000001");

    public ConfirmImportSessionEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator);
            // CompanyAdministrator is additionally required by the Automatic-mode scenarios added
            // below, which call PUT .../settings (company:manage) to switch the company into
            // Automatic employee-numbering mode before uploading/confirming an import.
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.CompanyAdministrator);
            // Employee is additionally required by this file's own CreateCompanyAsync test helper
            // (POST /api/companies, "role:employee" policy) — unrelated to DataImport's own
            // employee:manage policy, just a pre-existing setup helper this persona also needs.
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Ok_And_Creates_Employees_For_All_Valid_Rows()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        await EnsureDefaultLeavePolicyAsync(client, companyId);

        var sessionId = await UploadAsync(client, companyId, ValidCsv());
        var validateResponse = await client.PostAsync(
            $"/api/companies/{companyId}/data-import/sessions/{sessionId}/validate", EmptyJson());
        validateResponse.EnsureSuccessStatusCode();

        var response = await client.PostAsync(ConfirmUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ConfirmPayload>();
        Assert.NotNull(payload);
        Assert.Equal(sessionId, payload!.ImportSessionId);
        Assert.Equal("Imported", payload.Status);
        Assert.Equal(2, payload.CreatedCount);
        Assert.Equal(0, payload.FailedCount);

        var employeesResponse = await client.GetAsync($"/api/companies/{companyId}/employees?pageSize=50");
        employeesResponse.EnsureSuccessStatusCode();
        var employees = await employeesResponse.Content.ReadFromJsonAsync<ListEmployeesPayload>();
        Assert.NotNull(employees);
        var john = Assert.Single(employees!.Items, e => e.WorkEmail == "john.doe@example.com");
        Assert.Contains(employees.Items, e => e.WorkEmail == "jane.doe@example.com");

        // Imported employees go through the same EmployeeCreatedIntegrationEvent (IsImported:
        // true) as directly-created ones — ConfirmImportSessionHandler publishes it once per
        // successfully-imported row (see EmployeeImportWriter's own doc comment) — so an "Employee
        // joined" timeline entry must exist here too, with the import-specific summary text.
        var timelineResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{john.Id}/timeline");
        timelineResponse.EnsureSuccessStatusCode();
        var timeline = await timelineResponse.Content.ReadFromJsonAsync<TimelinePayload>();
        Assert.NotNull(timeline);
        var joinedEntry = Assert.Single(timeline!.Items, i => i.EventType == "EmployeeJoined");
        Assert.Contains("imported", joinedEntry.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(ConfirmUrl(Guid.NewGuid(), Guid.NewGuid()), EmptyJson());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_For_Unknown_Session()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.PostAsync(ConfirmUrl(companyId, Guid.NewGuid()), EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_When_Company_Claim_Mismatches_Route()
    {
        var companyId = Guid.NewGuid();
        using var uploadClient = await AdminClient(companyId);
        var sessionId = await UploadAsync(uploadClient, companyId, ValidCsv());

        using var mismatchedClient = _factory.CreateClient();
        mismatchedClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        mismatchedClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());

        // Route company differs from the authenticated user's company_id claim (cross-tenant).
        var response = await mismatchedClient.PostAsync(ConfirmUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_When_Import_Session_Belongs_To_A_Different_Company()
    {
        var ownerCompanyId = Guid.NewGuid();
        var callerCompanyId = Guid.NewGuid();

        using var ownerClient = await AdminClient(ownerCompanyId);
        var sessionId = await UploadAsync(ownerClient, ownerCompanyId, ValidCsv());
        var validateResponse = await ownerClient.PostAsync(
            $"/api/companies/{ownerCompanyId}/data-import/sessions/{sessionId}/validate", EmptyJson());
        validateResponse.EnsureSuccessStatusCode();

        using var callerClient = await AdminClient(callerCompanyId);

        // Caller's claim matches the route company (passes the auth check), but the session
        // was created under a different company, so the handler cannot find it for this caller.
        var response = await callerClient.PostAsync(ConfirmUrl(callerCompanyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Conflict_When_Session_Has_Not_Been_Validated_Yet()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        // Uploaded but never validated: session is still Pending, which is not a confirmable state.
        var sessionId = await UploadAsync(client, companyId, ValidCsv());

        var response = await client.PostAsync(ConfirmUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Conflict_When_Session_Has_Already_Been_Confirmed()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        await EnsureDefaultLeavePolicyAsync(client, companyId);

        var sessionId = await UploadAsync(client, companyId, ValidCsv());
        await client.PostAsync($"/api/companies/{companyId}/data-import/sessions/{sessionId}/validate", EmptyJson());

        var firstConfirm = await client.PostAsync(ConfirmUrl(companyId, sessionId), EmptyJson());
        Assert.Equal(HttpStatusCode.OK, firstConfirm.StatusCode);

        // A session that landed on "Imported" (zero failures) is no longer in a confirmable
        // state, since Validated/CompletedWithErrors are the only two accepted statuses.
        var secondConfirm = await client.PostAsync(ConfirmUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.Conflict, secondConfirm.StatusCode);
    }

    [Fact]
    public async Task Automatic_Mode_Row_With_Supplied_EmployeeNumber_Fails_Validation_Alone_While_Other_Rows_Succeed()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());
        // UpdateCompanySettings (used by SetEmployeeNumberModeAsync below) requires a real
        // companies.companies row — unlike upload/validate/confirm, which never check the Company
        // table directly and so can use an arbitrary companyId elsewhere in this file.
        var companyId = await CreateCompanyAsync(client);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        await EnsureDefaultLeavePolicyAsync(client, companyId);
        await SetEmployeeNumberModeAsync(client, companyId, "Automatic", prefix: "EMP-", nextEmployeeNumber: 1, minimumLength: 3);

        // Row 2 (John) leaves Employee Number blank, as required in Automatic mode. Row 3 (Jane)
        // incorrectly supplies one — per EmployeeStagingRowValidator, this is a row-level failure
        // for Jane's row alone (staging IsValid is per-row, not whole-batch atomic): the ticket's
        // "reject the whole batch, generate zero numbers" concern about a bad row poisoning an
        // otherwise-valid import doesn't apply here, because Jane's invalid row simply never
        // reaches the confirm/generation step at all — John's still-valid row proceeds normally
        // and gets a real generated number.
        const string csv =
            "First Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile\n" +
            "John,Doe,john.doe@example.com,2026-01-01,,1990-01-01,British,Male,Sales,London,Permanent,Software Developer\n" +
            "Jane,Doe,jane.doe@example.com,2026-01-02,EMP-999,1991-02-02,British,Female,Sales,London,Permanent,Software Developer\n";

        var sessionId = await UploadAsync(client, companyId, csv);
        var validateResponse = await client.PostAsync(
            $"/api/companies/{companyId}/data-import/sessions/{sessionId}/validate", EmptyJson());
        validateResponse.EnsureSuccessStatusCode();
        var validatePayload = await validateResponse.Content.ReadFromJsonAsync<ValidatePayload>();
        Assert.NotNull(validatePayload);
        Assert.Equal(1, validatePayload!.SuccessfulRows);
        Assert.Equal(1, validatePayload.FailedRows);

        var response = await client.PostAsync(ConfirmUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ConfirmPayload>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.CreatedCount);
        Assert.Equal(1, payload.FailedCount);

        var createdRow = Assert.Single(payload.CreatedRows);
        Assert.Equal(2, createdRow.RowNumber);
        Assert.Equal("EMP-001", createdRow.EmployeeNumber);

        var employeesResponse = await client.GetAsync($"/api/companies/{companyId}/employees?pageSize=50");
        employeesResponse.EnsureSuccessStatusCode();
        var employees = await employeesResponse.Content.ReadFromJsonAsync<ListEmployeesPayload>();
        Assert.NotNull(employees);
        Assert.Contains(employees!.Items, e => e.WorkEmail == "john.doe@example.com");
        Assert.DoesNotContain(employees.Items, e => e.WorkEmail == "jane.doe@example.com");
    }

    [Fact]
    public async Task Automatic_Mode_Confirm_Assigns_Generated_Numbers_To_All_Valid_Rows_In_RowOrder()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());
        var companyId = await CreateCompanyAsync(client);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        await EnsureDefaultLeavePolicyAsync(client, companyId);
        await SetEmployeeNumberModeAsync(client, companyId, "Automatic", prefix: "EMP-", nextEmployeeNumber: 1, minimumLength: 3);

        const string csv =
            "First Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile\n" +
            "John,Doe,john.doe@example.com,2026-01-01,,1990-01-01,British,Male,Sales,London,Permanent,Software Developer\n" +
            "Jane,Doe,jane.doe@example.com,2026-01-02,,1991-02-02,British,Female,Sales,London,Permanent,Software Developer\n";

        var sessionId = await UploadAsync(client, companyId, csv);
        var validateResponse = await client.PostAsync(
            $"/api/companies/{companyId}/data-import/sessions/{sessionId}/validate", EmptyJson());
        validateResponse.EnsureSuccessStatusCode();

        var response = await client.PostAsync(ConfirmUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ConfirmPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.CreatedCount);
        Assert.Equal(0, payload.FailedCount);
        Assert.Equal(2, payload.CreatedRows.Count);
        Assert.Equal(2, payload.CreatedRows[0].RowNumber);
        Assert.Equal("EMP-001", payload.CreatedRows[0].EmployeeNumber);
        Assert.Equal(3, payload.CreatedRows[1].RowNumber);
        Assert.Equal("EMP-002", payload.CreatedRows[1].EmployeeNumber);

        // The confirm response reports what the writer/generator returned in-memory — re-fetch
        // each employee to prove the generated number was actually persisted, not just echoed back.
        var johnResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{payload.CreatedRows[0].EmployeeId}");
        johnResponse.EnsureSuccessStatusCode();
        var john = await johnResponse.Content.ReadFromJsonAsync<EmployeeDetailPayload>();
        Assert.Equal("EMP-001", john!.EmployeeNumber);

        var janeResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{payload.CreatedRows[1].EmployeeId}");
        janeResponse.EnsureSuccessStatusCode();
        var jane = await janeResponse.Content.ReadFromJsonAsync<EmployeeDetailPayload>();
        Assert.Equal("EMP-002", jane!.EmployeeNumber);
    }

    private static async Task<Guid> CreateCompanyAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/companies", new
        {
            name = $"Import EmployeeNumber Test Co {Guid.NewGuid():N}",
            addresses = new[]
            {
                new { type = "RegisteredOffice", line1 = "10 High Street", city = "London", countryCode = "GB" }
            }
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    // Was calling PUT /api/companies/{id}/settings (UpdateCompanySettingsHandler), which only
    // persists TimeZone/Locale and silently ignores every other field in the request body
    // (including employeeNumberMode) — it still returned 200 OK. The actual employee-number/HR
    // settings live behind PUT /api/companies/{id}/hr-settings (UpdateHrSettingsHandler).
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

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, ImportAdmin, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    /// <summary>
    /// DefaultLeavePolicyId is now mandatory on PositionProfile, so
    /// ImportLookupResolver.GetOrCreatePositionProfileAsync can only auto-create a position
    /// profile for a CSV row when the company already has a default leave policy configured
    /// (the first policy created for a company is automatically its default — see
    /// CreateLeavePolicyHandler). Without this, rows referencing a not-yet-existing position
    /// profile are silently skipped and the row fails.
    /// </summary>
    private static async Task EnsureDefaultLeavePolicyAsync(HttpClient client, Guid companyId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Default Policy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        response.EnsureSuccessStatusCode();
    }

    private static string ConfirmUrl(Guid companyId, Guid sessionId) =>
        $"/api/companies/{companyId}/data-import/sessions/{sessionId}/confirm";

    private static string ValidCsv() =>
        "First Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile\n" +
        "John,Doe,john.doe@example.com,2026-01-01,EMP001,1990-01-01,British,Male,Sales,London,Permanent,Software Developer\n" +
        "Jane,Doe,jane.doe@example.com,2026-01-02,EMP002,1991-02-02,British,Female,Sales,London,Permanent,Software Developer\n";

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

        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csvContent));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
        content.Add(fileContent, "File", "employees.csv");

        return content;
    }

    private static StringContent EmptyJson() => new("{}", Encoding.UTF8, "application/json");

    private sealed record UploadPayload(
        Guid Id, Guid CompanyId, string EntityType, string FileName, string Status,
        int TotalRows, DateTimeOffset CreatedAt);

    private sealed record IdPayload(Guid Id);

    private sealed record ConfirmedRowPayload(int RowNumber, Guid EmployeeId, string EmployeeNumber);

    private sealed record EmployeeDetailPayload(Guid Id, string? EmployeeNumber);

    private sealed record ConfirmPayload(
        Guid ImportSessionId, string Status, int CreatedCount, int FailedCount,
        List<ConfirmedRowPayload> CreatedRows);

    private sealed record ValidatePayload(
        Guid Id, string Status, int TotalRows, int SuccessfulRows, int FailedRows);

    private sealed record EmployeeListItemPayload(Guid Id, string WorkEmail);

    private sealed record ListEmployeesPayload(
        List<EmployeeListItemPayload> Items, int TotalCount, int PageNumber, int PageSize, int TotalPages);

    private sealed record TimelineItemPayload(string EventType, string Summary);

    private sealed record TimelinePayload(List<TimelineItemPayload> Items, int TotalCount);
}
