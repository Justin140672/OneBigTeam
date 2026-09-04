using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// OBT-REM-08: end-to-end coverage for ConfirmImportSession's resumable confirmation behaviour,
/// running against a real Postgres testcontainer (see ApiWebApplicationFactory) rather than the
/// in-memory-DbContext handler tests in HR.Modules.DataImport.Tests/ConfirmImportSessionHardeningTests.cs.
///
/// There is no built-in fault-injection hook for "crash between employee creation and the
/// downstream steps" at the HTTP boundary, so the crash/partial-progress and stale-claim scenarios
/// here are simulated by reaching into the real DataImportDbContext via the test host's DI
/// container (the same pattern AuditHistoryIntegrationTests uses for direct DbContext reads) and
/// calling the (InternalsVisibleTo-exposed) internal domain methods directly - never by editing
/// production code to add test seams.
/// </summary>
[Collection("Integration")]
public class ConfirmImportSessionResumabilityEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ImportAdmin = Guid.Parse("59000000-0000-0000-0000-0000000000BB");

    public ConfirmImportSessionResumabilityEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Simultaneous_Confirm_Requests_Only_One_Succeeds_And_Only_One_Employee_Is_Created()
    {
        var (client, companyId) = await ManualModeAdminClientAsync();
        var sessionId = await UploadAsync(client, companyId, ValidCsv());
        (await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson())).EnsureSuccessStatusCode();

        // Two independent clients (same auth) racing the same confirm call. The xmin concurrency
        // token on ImportSession.ClaimForConfirmation means only one of these can win the save
        // that transitions the session into Processing.
        using var clientA = await AdminClientAsync(companyId);
        using var clientB = await AdminClientAsync(companyId);

        var taskA = clientA.PostAsync(ConfirmUrl(companyId, sessionId), EmptyJson());
        var taskB = clientB.PostAsync(ConfirmUrl(companyId, sessionId), EmptyJson());
        var responses = await Task.WhenAll(taskA, taskB);

        var statusCodes = responses.Select(r => r.StatusCode).OrderBy(s => s).ToList();
        // One request wins (200 OK); the loser either loses the optimistic-concurrency race (409)
        // or - if it arrives after the winner has already fully completed the session - is rejected
        // for the same "not confirmable" reason (also 409). Either way exactly one must succeed.
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);

        var employeesResponse = await client.GetAsync($"/api/companies/{companyId}/employees?pageSize=50");
        employeesResponse.EnsureSuccessStatusCode();
        var employees = await employeesResponse.Content.ReadFromJsonAsync<ListEmployeesPayload>();
        Assert.NotNull(employees);
        // ValidCsv() has two rows; the loser must not have written a second set of employees.
        Assert.Equal(2, employees!.Items.Count);
    }

    [Fact]
    public async Task Stale_Processing_Claim_Older_Than_15_Minutes_Is_Reclaimable_And_Completes_The_Import()
    {
        var (client, companyId) = await ManualModeAdminClientAsync();
        var sessionId = await UploadAsync(client, companyId, ValidCsv());
        (await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson())).EnsureSuccessStatusCode();

        // Simulate a prior confirm attempt that claimed the session (Processing) 20 minutes ago and
        // then crashed before finishing - older than the handler's 15-minute stale-claim window.
        await MutateSessionAsync(sessionId, session => session.Start(DateTimeOffset.UtcNow.AddMinutes(-20)));

        var beforeConfirm = DateTimeOffset.UtcNow;
        var response = await client.PostAsync(ConfirmUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ConfirmPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Imported", payload!.Status);
        Assert.Equal(2, payload.CreatedCount);

        // The bug this guards against: StartedAt used to only ever be set once (??=), so a reclaimed
        // stale session would keep reporting its StartedAt frozen at the original crash time. It
        // must now reflect this (successful) attempt's own claim time.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataImportDbContext>();
        var saved = await db.ImportSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        Assert.NotNull(saved.StartedAt);
        Assert.True(saved.StartedAt >= beforeConfirm.AddSeconds(-5));
    }

    [Fact]
    public async Task Recently_Claimed_Processing_Session_Is_Not_Confirmable_Even_If_Validated_Long_Ago()
    {
        var (client, companyId) = await ManualModeAdminClientAsync();
        var sessionId = await UploadAsync(client, companyId, ValidCsv());
        (await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson())).EnsureSuccessStatusCode();

        // Actively running: claimed 5 minutes ago, well inside the stale window, so a competing
        // confirm request must be rejected rather than treated as an abandoned claim.
        await MutateSessionAsync(sessionId, session => session.Start(DateTimeOffset.UtcNow.AddMinutes(-5)));

        var response = await client.PostAsync(ConfirmUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Retry_After_Simulated_Crash_Between_Employee_Creation_And_Downstream_Steps_Resumes_Without_Duplicate_Employee()
    {
        var (client, companyId) = await ManualModeAdminClientAsync();
        // Single-row file keeps the "exactly one employee, ever" assertion unambiguous.
        const string csv =
            "First Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile,Salary Amount\n" +
            "John,Doe,john.resume@example.com,2026-01-01,EMP001,1990-01-01,British,Male,Sales,London,Permanent,Software Developer,50000\n";
        var sessionId = await UploadAsync(client, companyId, csv);
        (await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson())).EnsureSuccessStatusCode();

        var firstConfirm = await client.PostAsync(ConfirmUrl(companyId, sessionId), EmptyJson());
        Assert.Equal(HttpStatusCode.OK, firstConfirm.StatusCode);
        var firstPayload = await firstConfirm.Content.ReadFromJsonAsync<ConfirmPayload>();
        Assert.NotNull(firstPayload);
        Assert.Equal("Imported", firstPayload!.Status);
        Assert.Equal(1, firstPayload.CreatedCount);

        Guid createdEmployeeId = default;

        // Roll the row's durable per-step progress (and the session status) back to what it would
        // have looked like immediately after MarkEmployeeCreated persisted but before any of the
        // downstream steps (events / opening leave balance / manager assignment) ran - simulating a
        // crash at that exact point. CreatedEmployeeId is deliberately left untouched: the whole
        // point of the resume path is that it is never cleared or re-derived.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DataImportDbContext>();
            var row = await db.ImportStagingEmployees.SingleAsync(
                s => s.ImportSessionId == sessionId && s.WorkEmail == "john.resume@example.com");
            createdEmployeeId = row.CreatedEmployeeId!.Value;

            // Property() bypasses the entity's private setters (this is EF Core's standard escape
            // hatch for exactly this kind of test-only state manipulation), letting the test force
            // the row back into a "partially resumed" shape without any production code change.
            db.Entry(row).Property(nameof(ImportStagingEmployee.EmployeeCreatedEventPublishedAt)).CurrentValue = null;
            db.Entry(row).Property(nameof(ImportStagingEmployee.EmployeeImportedEventPublishedAt)).CurrentValue = null;
            db.Entry(row).Property(nameof(ImportStagingEmployee.OpeningLeaveBalanceProcessedAt)).CurrentValue = null;
            db.Entry(row).Property(nameof(ImportStagingEmployee.ManagerAssignmentProcessedAt)).CurrentValue = null;
            db.Entry(row).Property(nameof(ImportStagingEmployee.FullyConfirmedAt)).CurrentValue = null;

            var session = await db.ImportSessions.SingleAsync(s => s.Id == sessionId);
            session.Confirm(createdCount: 0, failedCount: 1, DateTimeOffset.UtcNow);

            await db.SaveChangesAsync();
        }

        var retryConfirm = await client.PostAsync(ConfirmUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.OK, retryConfirm.StatusCode);
        var retryPayload = await retryConfirm.Content.ReadFromJsonAsync<ConfirmPayload>();
        Assert.NotNull(retryPayload);
        Assert.Equal("Imported", retryPayload!.Status);
        Assert.Equal(1, retryPayload.CreatedCount);
        Assert.Equal(0, retryPayload.FailedCount);

        var employeesResponse = await client.GetAsync($"/api/companies/{companyId}/employees?pageSize=50");
        employeesResponse.EnsureSuccessStatusCode();
        var employees = await employeesResponse.Content.ReadFromJsonAsync<ListEmployeesPayload>();
        Assert.NotNull(employees);
        // Exactly one employee for this work email - the retry must not have created a duplicate.
        Assert.Single(employees!.Items, e => e.WorkEmail == "john.resume@example.com");

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<DataImportDbContext>();
        var savedRow = await verifyDb.ImportStagingEmployees.AsNoTracking().SingleAsync(
            s => s.ImportSessionId == sessionId && s.WorkEmail == "john.resume@example.com");
        Assert.Equal(createdEmployeeId, savedRow.CreatedEmployeeId); // same employee id - never recreated
        Assert.NotNull(savedRow.EmployeeCreatedEventPublishedAt);
        Assert.NotNull(savedRow.EmployeeImportedEventPublishedAt);
        Assert.NotNull(savedRow.OpeningLeaveBalanceProcessedAt);
        Assert.NotNull(savedRow.ManagerAssignmentProcessedAt);
        Assert.True(savedRow.IsFullyConfirmed);
    }

    private async Task MutateSessionAsync(Guid sessionId, Action<ImportSession> mutate)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataImportDbContext>();
        var session = await db.ImportSessions.SingleAsync(s => s.Id == sessionId);
        mutate(session);
        await db.SaveChangesAsync();
    }

    private async Task<(HttpClient Client, Guid CompanyId)> ManualModeAdminClientAsync()
    {
        using var bootstrapClient = _factory.CreateClient();
        bootstrapClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        bootstrapClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Resumability Test Co {Guid.NewGuid():N}");

        var client = await AdminClientAsync(companyId);
        await EnsureDefaultLeavePolicyAsync(client, companyId);
        await SetEmployeeNumberModeAsync(client, companyId, "Manual");
        return (client, companyId);
    }

    private async Task<HttpClient> AdminClientAsync(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, ImportAdmin, SystemRoles.HrAdministrator, companyId);
        return client;
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

    private static string ValidCsv() =>
        "First Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile,Salary Amount\n" +
        "John,Doe,john.doe@example.com,2026-01-01,EMP001,1990-01-01,British,Male,Sales,London,Permanent,Software Developer,50000\n" +
        "Jane,Doe,jane.doe@example.com,2026-01-02,EMP002,1991-02-02,British,Female,Sales,London,Permanent,Software Developer,50000\n";

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

    private sealed record ConfirmPayload(Guid ImportSessionId, string Status, int CreatedCount, int FailedCount);

    private sealed record EmployeeListItemPayload(Guid Id, string WorkEmail);

    private sealed record ListEmployeesPayload(
        List<EmployeeListItemPayload> Items, int TotalCount, int PageNumber, int PageSize, int TotalPages);
}
