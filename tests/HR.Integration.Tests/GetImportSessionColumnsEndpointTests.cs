using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetImportSessionColumnsEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ImportAdmin = Guid.Parse("60000000-0000-0000-0000-000000000001");

    public GetImportSessionColumnsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Ok_With_Detected_Headers_After_Uploading_A_Session()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var sessionId = await UploadAsync(client, companyId, ValidCsv());

        var response = await client.GetAsync(ColumnsUrl(companyId, sessionId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ColumnsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(sessionId, payload!.ImportSessionId);
        Assert.Contains("First Name", payload.DetectedHeaders);
        Assert.Contains("Last Name", payload.DetectedHeaders);
        Assert.NotEmpty(payload.FieldSuggestions);
        Assert.Contains(
            payload.FieldSuggestions,
            s => s.TargetField == "FirstName" && s.SuggestedHeader == "First Name");
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(ColumnsUrl(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_When_Session_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.GetAsync(ColumnsUrl(companyId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_When_Session_Belongs_To_A_Different_Company()
    {
        var ownerCompanyId = Guid.NewGuid();
        var callerCompanyId = Guid.NewGuid();

        using var ownerClient = AdminClient(ownerCompanyId);
        var sessionId = await UploadAsync(ownerClient, ownerCompanyId, ValidCsv());

        using var callerClient = AdminClient(callerCompanyId);

        var response = await callerClient.GetAsync(ColumnsUrl(callerCompanyId, sessionId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private static string ColumnsUrl(Guid companyId, Guid sessionId) =>
        $"/api/companies/{companyId}/data-import/sessions/{sessionId}/columns";

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

        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csvContent));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
        content.Add(fileContent, "File", "employees.csv");

        return content;
    }

    private sealed record UploadPayload(
        Guid Id, Guid CompanyId, string EntityType, string FileName, string Status,
        int TotalRows, DateTimeOffset CreatedAt);

    private sealed record FieldSuggestionPayload(string TargetField, string StandardHeaderName, string? SuggestedHeader);

    private sealed record ColumnsPayload(
        Guid ImportSessionId, List<string> DetectedHeaders, List<FieldSuggestionPayload> FieldSuggestions);
}
