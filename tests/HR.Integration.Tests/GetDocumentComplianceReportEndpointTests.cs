using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetDocumentComplianceReportEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public GetDocumentComplianceReportEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Get_DocumentComplianceReport_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/reporting/document-compliance");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_DocumentComplianceReport_Returns_Forbidden_For_Manager()
    {
        // reporting:view-hr only.
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/document-compliance");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_DocumentComplianceReport_Returns_Ok_With_Empty_Items_For_HrAdministrator()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/document-compliance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_DocumentComplianceReport_Returns_Ok_When_Filtered_By_PositionProfileId()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/document-compliance?positionProfileId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_DocumentComplianceReport_Returns_UnprocessableEntity_For_Invalid_CompanyId()
    {
        var userId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, Guid.Empty);

        var response = await client.GetAsync($"/api/companies/{Guid.Empty}/reporting/document-compliance");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record ReportPayload(List<ReportItemPayload> Items, int TotalEmployees, int TotalMissing, int TotalExpiringSoon, int TotalExpired);

    private sealed record ReportItemPayload(
        Guid EmployeeId,
        string EmployeeName,
        int RequiredCount,
        int UploadedCount,
        int MissingCount,
        int ExpiringSoonCount,
        int ExpiredCount,
        List<string> MissingDocumentTypeNames);
}
