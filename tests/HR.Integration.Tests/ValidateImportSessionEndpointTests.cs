using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class ValidateImportSessionEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ImportAdmin = Guid.Parse("57000000-0000-0000-0000-000000000001");

    public ValidateImportSessionEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
                await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Ok_With_Expected_Response_After_Uploading_And_Validating_A_Valid_File()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var sessionId = await UploadAsync(client, companyId, ValidCsv());

        var response = await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ValidatePayload>();
        Assert.NotNull(payload);
        Assert.Equal(sessionId, payload!.Id);
        Assert.Equal("Completed", payload.Status);
        Assert.Equal(2, payload.TotalRows);
        Assert.Equal(2, payload.SuccessfulRows);
        Assert.Equal(0, payload.FailedRows);
    }

    [Fact]
    public async Task Returns_Ok_With_CompletedWithErrors_When_A_Row_Is_Invalid()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        // Second data row is missing a required "Last Name" value.
        const string csv =
            "First Name,Last Name,Work Email,Start Date,Employee Number\n" +
            "John,Doe,john.doe@example.com,2026-01-01,EMP001\n" +
            "Jane,,jane.doe@example.com,2026-01-02,EMP002\n";

        var sessionId = await UploadAsync(client, companyId, csv);

        var response = await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ValidatePayload>();
        Assert.NotNull(payload);
        Assert.Equal("CompletedWithErrors", payload!.Status);
        Assert.Equal(2, payload.TotalRows);
        Assert.Equal(1, payload.SuccessfulRows);
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
        using var uploadClient = AdminClient(companyId);
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

        using var ownerClient = AdminClient(ownerCompanyId);
        var sessionId = await UploadAsync(ownerClient, ownerCompanyId, ValidCsv());

        using var callerClient = AdminClient(callerCompanyId);

        // Caller's claim matches the route company (passes the auth check), but the session
        // was created under a different company, so the handler cannot find it for this caller.
        var response = await callerClient.PostAsync(ValidateUrl(callerCompanyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Conflict_When_Session_Has_Already_Been_Validated()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var sessionId = await UploadAsync(client, companyId, ValidCsv());

        var firstResponse = await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson());
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson());

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private static string ValidateUrl(Guid companyId, Guid sessionId) =>
        $"/api/companies/{companyId}/data-import/sessions/{sessionId}/validate";

    private static string ValidCsv() =>
        "First Name,Last Name,Work Email,Start Date,Employee Number\n" +
        "John,Doe,john.doe@example.com,2026-01-01,EMP001\n" +
        "Jane,Doe,jane.doe@example.com,2026-01-02,EMP002\n";

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
