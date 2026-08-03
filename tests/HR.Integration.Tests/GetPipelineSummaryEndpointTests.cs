using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetPipelineSummaryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc000005-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc000005-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetPipelineSummaryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployeeUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private HttpClient ClientAs(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Get_PipelineSummary_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/recruitment/pipeline-summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_PipelineSummary_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment/pipeline-summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_PipelineSummary_Returns_UnprocessableEntity_For_Empty_CompanyId()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.Empty.ToString());

        var response = await client.GetAsync($"/api/companies/{Guid.Empty}/recruitment/pipeline-summary");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_PipelineSummary_Returns_No_Items_When_Company_Has_No_RecruitmentStages()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment/pipeline-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_PipelineSummary_Returns_Default_Stages_Zero_Filled_And_Excludes_Terminal_Stages_And_Withdrawn()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        await SeedAsync(scope =>
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var seeded = RecruitmentStageSeeder.BuildDefaultStages(companyId, Now);
            db.RecruitmentStages.AddRange(seeded);

            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Software Engineer", null, Guid.NewGuid(), Now);
            db.Vacancies.Add(vacancy);

            var applicationReceivedStageId = seeded.Single(s => s.Name == "Application Received").Id;
            var hiredStageId = seeded.Single(s => s.Name == "Hired").Id;

            var candidate1 = Candidate.Create(Guid.NewGuid(), companyId, "First", "Last1", $"c1.{Guid.NewGuid():N}@example.com", null, null, Now);
            var candidate2 = Candidate.Create(Guid.NewGuid(), companyId, "First", "Last2", $"c2.{Guid.NewGuid():N}@example.com", null, null, Now);
            var candidate3 = Candidate.Create(Guid.NewGuid(), companyId, "First", "Last3", $"c3.{Guid.NewGuid():N}@example.com", null, null, Now);
            db.Candidates.AddRange(candidate1, candidate2, candidate3);

            var applied = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate1.Id, applicationReceivedStageId, null, Now);
            var withdrawn = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate2.Id, applicationReceivedStageId, null, Now);
            withdrawn.Withdraw(Now);
            var hired = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate3.Id, hiredStageId, null, Now);
            db.Applications.AddRange(applied, withdrawn, hired);
        });

        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment/pipeline-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(payload);
        Assert.Equal(
            ["Application Received", "CV Review", "Interview", "Offer"],
            payload!.Items.Select(i => i.Status).ToArray());
        Assert.DoesNotContain(payload.Items, i => i.Status is "Hired" or "Rejected");

        var appliedItem = Assert.Single(payload.Items, i => i.Status == "Application Received");
        Assert.Equal(1, appliedItem.ApplicationCount);
        Assert.Equal(1, payload.Items.Sum(i => i.ApplicationCount));
    }

    [Fact]
    public async Task Get_PipelineSummary_Isolates_By_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        await SeedAsync(scope =>
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var seeded = RecruitmentStageSeeder.BuildDefaultStages(otherCompanyId, Now);
            db.RecruitmentStages.AddRange(seeded);
            var applicationReceivedStageId = seeded.Single(s => s.Name == "Application Received").Id;

            var otherVacancy = Vacancy.Create(Guid.NewGuid(), otherCompanyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
            var otherCandidate = Candidate.Create(Guid.NewGuid(), otherCompanyId, "First", "Last", $"c.{Guid.NewGuid():N}@example.com", null, null, Now);
            db.Vacancies.Add(otherVacancy);
            db.Candidates.Add(otherCandidate);
            db.Applications.Add(Application.Create(Guid.NewGuid(), otherCompanyId, otherVacancy.Id, otherCandidate.Id, applicationReceivedStageId, null, Now));
        });

        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment/pipeline-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private async Task SeedAsync(Action<IServiceScope> seed)
    {
        using var scope = _factory.Services.CreateScope();
        seed(scope);
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        await db.SaveChangesAsync();
    }

    private sealed record SummaryPayload(List<SummaryItem> Items);
    private sealed record SummaryItem(string Status, int ApplicationCount);
}
