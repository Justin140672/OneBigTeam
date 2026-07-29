using System.Net;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class ExportRecruitmentPipelineReportEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public ExportRecruitmentPipelineReportEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Export_RecruitmentPipelineReport_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/reporting/recruitment-pipeline/export");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Export_RecruitmentPipelineReport_Returns_Forbidden_For_NonRecruiter()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/recruitment-pipeline/export");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_RecruitmentPipelineReport_Returns_Csv_For_Recruiter()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Recruiter);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/recruitment-pipeline/export?format=Csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Group,Vacancies,Applicants,Interviews,Offers,Hires", body);
    }

    [Fact]
    public async Task Export_RecruitmentPipelineReport_Returns_UnprocessableEntity_For_Invalid_GroupBy()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Recruiter);
        using var client = ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/recruitment-pipeline/export?groupBy=999");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
