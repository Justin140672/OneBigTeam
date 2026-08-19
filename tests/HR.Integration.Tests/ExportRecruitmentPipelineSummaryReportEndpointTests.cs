using System.Net;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ExportRecruitmentPipelineSummaryReportEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ExportRecruitmentPipelineSummaryReportEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Recruiter, companyId);
        return client;
    }

    private async Task SeedOpenVacancyWithStageAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var now = DateTimeOffset.UtcNow;

        var applicationReceived = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Application Received", 1, false, RecruitmentStageTerminalOutcome.None, now);
        db.RecruitmentStages.Add(applicationReceived);

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), now);
        vacancy.Open(now, new DateOnly(2026, 1, 1));
        db.Vacancies.Add(vacancy);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Export_RecruitmentPipelineSummary_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/reporting/recruitment-pipeline-summary/export");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Export_RecruitmentPipelineSummary_Returns_Forbidden_For_NonRecruiter()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/recruitment-pipeline-summary/export");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_RecruitmentPipelineSummary_Returns_Csv_With_Stage_Columns_For_Recruiter()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(userId, companyId);
        await SeedOpenVacancyWithStageAsync(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/recruitment-pipeline-summary/export?format=Csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Vacancy,Position Profile,Department,Status,Date Opened,Candidates,Application Received", body);
        Assert.Contains("Engineer", body);
    }

    [Fact]
    public async Task Export_RecruitmentPipelineSummary_Returns_UnprocessableEntity_For_Invalid_Format()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/recruitment-pipeline-summary/export?format=999");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Export_RecruitmentPipelineSummary_Isolates_By_Company()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await ClientFor(userId, companyId);
        await SeedOpenVacancyWithStageAsync(otherCompanyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/recruitment-pipeline-summary/export?format=Csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Engineer", body);
    }
}
