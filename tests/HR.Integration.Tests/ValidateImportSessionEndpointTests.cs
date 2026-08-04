using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ValidateImportSessionEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ImportAdmin = Guid.Parse("57000000-0000-0000-0000-000000000001");

    public ValidateImportSessionEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Ok_With_Expected_Response_After_Uploading_And_Validating_A_Valid_File()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        await EnsureDefaultLeavePolicyAsync(client, companyId);

        var sessionId = await UploadAsync(client, companyId, ValidCsv());

        var response = await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ValidatePayload>();
        Assert.NotNull(payload);
        Assert.Equal(sessionId, payload!.Id);
        Assert.Equal("Validated", payload.Status);
        Assert.Equal(2, payload.TotalRows);
        Assert.Equal(2, payload.SuccessfulRows);
        Assert.Equal(0, payload.FailedRows);
    }

    [Fact]
    public async Task Returns_Ok_And_Validated_When_One_Row_Is_Invalid_But_Another_Succeeds()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        // Second data row is missing a required "Last Name" value. One row is still valid, so
        // per ImportSession.Validate() the session lands on Validated (ready to confirm the
        // valid subset) rather than CompletedWithErrors (which is reserved for "every row failed").
        // Every other required field (including the mandatory Department/Location/Employment
        // Type/Position Profile lookups) is present on both rows so "Last Name" is the only
        // reason the second row fails.
        const string csv =
            "First Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile\n" +
            "John,Doe,john.doe@example.com,2026-01-01,EMP001,1990-01-01,British,Male,Sales,London,Permanent,Software Developer\n" +
            "Jane,,jane.doe@example.com,2026-01-02,EMP002,1991-02-02,British,Female,Sales,London,Permanent,Software Developer\n";

        await EnsureDefaultLeavePolicyAsync(client, companyId);
        var sessionId = await UploadAsync(client, companyId, csv);

        var response = await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ValidatePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Validated", payload!.Status);
        Assert.Equal(2, payload.TotalRows);
        Assert.Equal(1, payload.SuccessfulRows);
        Assert.Equal(1, payload.FailedRows);
    }

    [Fact]
    public async Task Returns_Ok_And_Validated_When_A_Row_References_A_New_Department()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        // Department, Location, Employment Type and Position Profile are all mandatory lookups
        // now — Location/Employment Type/Position Profile use pre-existing values here so the
        // only *new* reference data this row creates is the Department itself.
        const string csv =
            "First Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile\n" +
            "John,Doe,john.doe@example.com,2026-01-01,EMP001,1990-01-01,British,Male,Sales,London,Permanent,Software Developer\n" +
            "Jane,Doe,jane.doe@example.com,2026-01-02,EMP002,1991-02-02,British,Female,Sales,London,Permanent,Software Developer\n";

        await EnsureDefaultLeavePolicyAsync(client, companyId);
        var sessionId = await UploadAsync(client, companyId, csv);

        var response = await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ValidatePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Validated", payload!.Status);
        Assert.Equal(2, payload.TotalRows);
        Assert.Equal(2, payload.SuccessfulRows);
        Assert.Equal(0, payload.FailedRows);
    }

    [Fact]
    public async Task Returns_Ok_And_CompletedWithErrors_When_PositionProfile_Present_Without_Department_Or_Location()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        // Every other required field is present (including Employment Type) — Department and
        // Location are the only ones deliberately missing, so the row's single failure is
        // attributable to the Position Profile requiring both to be resolvable.
        const string csv =
            "First Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Employment Type,Position Profile\n" +
            "John,Doe,john.doe@example.com,2026-01-01,EMP001,1990-01-01,British,Male,Permanent,Software Developer\n";

        var sessionId = await UploadAsync(client, companyId, csv);

        var response = await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ValidatePayload>();
        Assert.NotNull(payload);
        Assert.Equal("CompletedWithErrors", payload!.Status);
        Assert.Equal(1, payload.TotalRows);
        Assert.Equal(0, payload.SuccessfulRows);
        Assert.Equal(1, payload.FailedRows);
    }

    [Fact]
    public async Task Returns_Ok_And_Validated_When_Department_Location_And_PositionProfile_All_New()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        const string csv =
            "First Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile\n" +
            "John,Doe,john.doe@example.com,2026-01-01,EMP001,1990-01-01,British,Male,Sales,London,Permanent,Software Developer\n";

        await EnsureDefaultLeavePolicyAsync(client, companyId);
        var sessionId = await UploadAsync(client, companyId, csv);

        var response = await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ValidatePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Validated", payload!.Status);
        Assert.Equal(1, payload.TotalRows);
        Assert.Equal(1, payload.SuccessfulRows);
        Assert.Equal(0, payload.FailedRows);
    }

    [Fact]
    public async Task Returns_Ok_And_Validated_When_ColumnMapping_Override_Maps_NonStandard_Headers()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        // Headers don't match the standard template ("Given Name"/"Family Name" instead of
        // "First Name"/"Last Name") — without a mapping override this would fail to populate
        // the required FirstName/LastName fields. Every other column uses its standard header
        // name (no override needed for those targets).
        const string csv =
            "Given Name,Family Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile\n" +
            "John,Doe,john.doe@example.com,2026-01-01,EMP001,1990-01-01,British,Male,Sales,London,Permanent,Software Developer\n";

        await EnsureDefaultLeavePolicyAsync(client, companyId);
        var sessionId = await UploadAsync(client, companyId, csv);

        var mappingBody = new StringContent(
            "{\"columnMapping\":{\"FirstName\":\"Given Name\",\"LastName\":\"Family Name\"}}",
            Encoding.UTF8, "application/json");

        var response = await client.PostAsync(ValidateUrl(companyId, sessionId), mappingBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ValidatePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Validated", payload!.Status);
        Assert.Equal(1, payload.TotalRows);
        Assert.Equal(1, payload.SuccessfulRows);
        Assert.Equal(0, payload.FailedRows);
    }

    [Fact]
    public async Task Returns_CompletedWithErrors_When_NonStandard_Headers_Used_Without_ColumnMapping_Override()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        // Same non-standard headers as the mapping-override test above, but posted with no
        // override — proves the override in that test is actually doing something, not a no-op.
        const string csv =
            "Given Name,Family Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile\n" +
            "John,Doe,john.doe@example.com,2026-01-01,EMP001,1990-01-01,British,Male,Sales,London,Permanent,Software Developer\n";

        var sessionId = await UploadAsync(client, companyId, csv);

        var response = await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ValidatePayload>();
        Assert.NotNull(payload);
        Assert.Equal("CompletedWithErrors", payload!.Status);
        Assert.Equal(1, payload.TotalRows);
        Assert.Equal(0, payload.SuccessfulRows);
        Assert.Equal(1, payload.FailedRows);
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            ValidateUrl(Guid.NewGuid(), Guid.NewGuid()),
            EmptyJson());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
        var response = await mismatchedClient.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_When_Import_Session_Belongs_To_A_Different_Company()
    {
        var ownerCompanyId = Guid.NewGuid();
        var callerCompanyId = Guid.NewGuid();

        using var ownerClient = await AdminClient(ownerCompanyId);
        var sessionId = await UploadAsync(ownerClient, ownerCompanyId, ValidCsv());

        using var callerClient = await AdminClient(callerCompanyId);

        // Caller's claim matches the route company (passes the auth check), but the session
        // was created under a different company, so the handler cannot find it for this caller.
        var response = await callerClient.PostAsync(ValidateUrl(callerCompanyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Conflict_When_Session_Has_Already_Been_Validated()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var sessionId = await UploadAsync(client, companyId, ValidCsv());

        var firstResponse = await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson());
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
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

    private static string ValidateUrl(Guid companyId, Guid sessionId) =>
        $"/api/companies/{companyId}/data-import/sessions/{sessionId}/validate";

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

    private sealed record UploadPayload(
        Guid Id, Guid CompanyId, string EntityType, string FileName, string Status,
        int TotalRows, DateTimeOffset CreatedAt);

    private sealed record ValidatePayload(
        Guid Id, string Status, int TotalRows, int SuccessfulRows, int FailedRows);

    private static StringContent EmptyJson() =>
        new("{}", Encoding.UTF8, "application/json");
}
