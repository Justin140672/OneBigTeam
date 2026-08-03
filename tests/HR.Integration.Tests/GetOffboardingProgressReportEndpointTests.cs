using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetOffboardingProgressReportEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetOffboardingProgressReportEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_OffboardingProgressReport_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/reporting/offboarding-progress");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_OffboardingProgressReport_Returns_Forbidden_For_Manager()
    {
        // reporting:view-hr only — sensitive HR data (access/asset return status), no manager
        // scoping, unlike GetOnboardingProgressReport/GetProbationReport.
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/offboarding-progress");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_OffboardingProgressReport_Returns_Ok_With_Empty_Items_For_HrAdministrator()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/offboarding-progress");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_OffboardingProgressReport_Returns_UnprocessableEntity_For_Invalid_CompanyId()
    {
        var userId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, Guid.Empty);

        var response = await client.GetAsync($"/api/companies/{Guid.Empty}/reporting/offboarding-progress");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record ReportPayload(List<ReportItemPayload> Items, int TotalEmployees, int OutstandingAccessCount, int OutstandingAssetsCount);

    private sealed record ReportItemPayload(
        Guid EmployeeId,
        string EmployeeName,
        DateOnly LastWorkingDay,
        string Status,
        List<string> OutstandingTasks,
        List<string> CompletedTasks,
        bool AccessDisabled,
        bool DocumentsReturned,
        bool AssetsReturned);
}
