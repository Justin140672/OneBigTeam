using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class ConfirmImportSessionEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ImportAdmin = Guid.Parse("59000000-0000-0000-0000-000000000001");

    public ConfirmImportSessionEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Ok_And_Creates_Employees_For_All_Valid_Rows()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
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
        Assert.Contains(employees!.Items, e => e.WorkEmail == "john.doe@example.com");
        Assert.Contains(employees.Items, e => e.WorkEmail == "jane.doe@example.com");
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
        using var client = AdminClient(companyId);

        var response = await client.PostAsync(ConfirmUrl(companyId, Guid.NewGuid()), EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_When_Company_Claim_Mismatches_Route()
    {
        var companyId = Guid.NewGuid();
        using var uploadClient = AdminClient(companyId);
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

        using var ownerClient = AdminClient(ownerCompanyId);
        var sessionId = await UploadAsync(ownerClient, ownerCompanyId, ValidCsv());
        var validateResponse = await ownerClient.PostAsync(
            $"/api/companies/{ownerCompanyId}/data-import/sessions/{sessionId}/validate", EmptyJson());
        validateResponse.EnsureSuccessStatusCode();

        using var callerClient = AdminClient(callerCompanyId);

        // Caller's claim matches the route company (passes the auth check), but the session
        // was created under a different company, so the handler cannot find it for this caller.
        var response = await callerClient.PostAsync(ConfirmUrl(callerCompanyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Conflict_When_Session_Has_Not_Been_Validated_Yet()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        // Uploaded but never validated: session is still Pending, which is not a confirmable state.
        var sessionId = await UploadAsync(client, companyId, ValidCsv());

        var response = await client.PostAsync(ConfirmUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Conflict_When_Session_Has_Already_Been_Confirmed()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
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

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
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

    private sealed record ConfirmPayload(Guid ImportSessionId, string Status, int CreatedCount, int FailedCount);

    private sealed record EmployeeListItemPayload(Guid Id, string WorkEmail);

    private sealed record ListEmployeesPayload(
        List<EmployeeListItemPayload> Items, int TotalCount, int PageNumber, int PageSize, int TotalPages);
}
