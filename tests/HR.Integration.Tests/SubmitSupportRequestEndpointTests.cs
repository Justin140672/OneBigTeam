using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class SubmitSupportRequestEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid EmployeeUserId = Guid.Parse("60000000-0000-0000-0000-000000000001");

    public SubmitSupportRequestEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> EmployeeClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EmployeeUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        // SubmitSupportRequest is gated behind "support:manage", not just role:employee.
        await TestRoleSeeder.AssignRoleAsync(_factory, EmployeeUserId, SystemRoles.Employee, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, EmployeeUserId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private static MultipartFormDataContent BuildRequest(
        Guid companyId,
        string title = "Leave balance not updating",
        string description = "The balance does not refresh after approving a leave request.",
        string type = "ReportProblem",
        string priority = "Medium",
        bool includeDiagnostics = true,
        bool includeFile = false)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(companyId.ToString()), "CompanyId" },
            { new StringContent(type), "Type" },
            { new StringContent(title), "Title" },
            { new StringContent(description), "Description" },
            { new StringContent(priority), "Priority" },
            { new StringContent(includeDiagnostics.ToString()), "IncludeDiagnostics" },
        };

        if (includeFile)
        {
            var fileContent = new ByteArrayContent([0x1, 0x2, 0x3, 0x4]);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
            content.Add(fileContent, "Files", "screenshot.png");
        }

        return content;
    }

    [Fact]
    public async Task Post_SupportRequests_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/api/companies/{Guid.NewGuid()}/support/requests",
            BuildRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_SupportRequests_Creates_Request_With_ReferenceNumber()
    {
        var companyId = Guid.NewGuid();
        using var client = await EmployeeClient(companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/support/requests",
            BuildRequest(companyId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<SubmitSupportRequestPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.StartsWith("SUP-", payload.ReferenceNumber);
    }

    [Fact]
    public async Task Post_SupportRequests_Persists_Attachment_When_File_Provided()
    {
        var companyId = Guid.NewGuid();
        using var client = await EmployeeClient(companyId);

        var created = await client.PostAsync(
            $"/api/companies/{companyId}/support/requests",
            BuildRequest(companyId, includeFile: true));
        created.EnsureSuccessStatusCode();
        var payload = await created.Content.ReadFromJsonAsync<SubmitSupportRequestPayload>();
        Assert.NotNull(payload);

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/support/requests/{payload!.Id}");
        getResponse.EnsureSuccessStatusCode();
        var detail = await getResponse.Content.ReadFromJsonAsync<GetSupportRequestPayload>();
        Assert.NotNull(detail);
        Assert.Single(detail!.Attachments);
        Assert.Equal("screenshot.png", detail.Attachments[0].FileName);
    }

    [Fact]
    public async Task Post_SupportRequests_Returns_UnprocessableEntity_When_Title_Is_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = await EmployeeClient(companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/support/requests",
            BuildRequest(companyId, title: ""));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_SupportRequests_Returns_UnprocessableEntity_When_Description_Is_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = await EmployeeClient(companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/support/requests",
            BuildRequest(companyId, description: ""));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_SupportRequests_Returns_Forbidden_When_Company_Claim_Mismatches()
    {
        var companyId = Guid.NewGuid();
        using var client = await EmployeeClient(companyId);

        var otherCompanyId = Guid.NewGuid();
        var response = await client.PostAsync(
            $"/api/companies/{otherCompanyId}/support/requests",
            BuildRequest(otherCompanyId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record SubmitSupportRequestPayload(Guid Id, string ReferenceNumber);

    private sealed record GetSupportRequestPayload(
        Guid Id,
        string ReferenceNumber,
        List<AttachmentPayload> Attachments);

    private sealed record AttachmentPayload(Guid Id, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt);
}
