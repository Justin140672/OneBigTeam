using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetSicknessReportEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public GetSicknessReportEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_SicknessReport_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/reporting/sickness");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_SicknessReport_Returns_Forbidden_For_Manager()
    {
        // "reporting:view-hr" is HR-only — sickness is sensitive health data, deliberately not
        // extended to Managers (unlike the Leave Summary/Probation reports).
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/sickness");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_SicknessReport_Returns_Ok_For_HrAdministrator()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/sickness");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.NotNull(payload!.Items);
    }

    [Fact]
    public async Task Get_SicknessReport_Returns_UnprocessableEntity_For_Invalid_GroupBy()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/sickness?groupBy=999");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_SicknessReport_Returns_UnprocessableEntity_For_EndDate_Before_StartDate()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/sickness?startDate=2026-06-01&endDate=2026-05-01");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record ReportPayload(List<ReportItemPayload> Items);

    private sealed record ReportItemPayload(
        string GroupKey,
        string GroupLabel,
        int AbsenceCount,
        decimal DaysAbsent,
        int BradfordScore);
}
