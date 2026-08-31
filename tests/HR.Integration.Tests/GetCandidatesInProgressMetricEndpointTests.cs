using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetCandidatesInProgressMetricEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("db040002-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("db040002-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetCandidatesInProgressMetricEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployeeUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientAs(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private string Url(Guid companyId) => $"/api/companies/{companyId}/recruitment/metrics/candidates-in-progress";

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(Url(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(PlainEmployeeUser, companyId);
        var response = await client.GetAsync(Url(companyId));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Counts_Live_Apps_Across_Custom_Reordered_Pipeline_And_Count_Matches_Items()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var screen = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Screening call", 3, false, RecruitmentStageTerminalOutcome.None, Now);
            var task = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Take-home", 1, false, RecruitmentStageTerminalOutcome.None, Now);
            var placed = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Placed", 2, true, RecruitmentStageTerminalOutcome.Hired, Now);
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "SRE", null, Guid.NewGuid(), Now);
            var c1 = Candidate.Create(Guid.NewGuid(), companyId, "Ana", "Ng", $"ana.{Guid.NewGuid():N}@example.com", null, null, Now);
            var c2 = Candidate.Create(Guid.NewGuid(), companyId, "Bo", "Li", $"bo.{Guid.NewGuid():N}@example.com", null, null, Now);
            var c3 = Candidate.Create(Guid.NewGuid(), companyId, "Cy", "Fox", $"cy.{Guid.NewGuid():N}@example.com", null, null, Now);
            var c4 = Candidate.Create(Guid.NewGuid(), companyId, "Di", "Ma", $"di.{Guid.NewGuid():N}@example.com", null, null, Now);
            var withdrawn = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, c3.Id, screen.Id, null, Now);
            withdrawn.Withdraw(Now);
            db.RecruitmentStages.AddRange(screen, task, placed);
            db.Vacancies.Add(vacancy);
            db.Candidates.AddRange(c1, c2, c3, c4);
            db.Applications.AddRange(
                Application.Create(Guid.NewGuid(), companyId, vacancy.Id, c1.Id, screen.Id, null, Now),
                Application.Create(Guid.NewGuid(), companyId, vacancy.Id, c2.Id, task.Id, null, Now),
                withdrawn,
                Application.Create(Guid.NewGuid(), companyId, vacancy.Id, c4.Id, placed.Id, null, Now));
            await db.SaveChangesAsync();
        }

        var payload = await (await client.GetAsync(Url(companyId))).Content.ReadFromJsonAsync<MetricPayload>();

        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Count);
        Assert.Equal(payload.Count, payload.Items.Count);
    }

    [Fact]
    public async Task Isolates_By_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var stages = RecruitmentStageSeeder.BuildDefaultStages(otherCompanyId, Now).ToList();
            var cv = stages.Single(s => s.Name == "CV Review");
            var vacancy = Vacancy.Create(Guid.NewGuid(), otherCompanyId, Guid.NewGuid(), "SRE", null, Guid.NewGuid(), Now);
            var cand = Candidate.Create(Guid.NewGuid(), otherCompanyId, "Fi", "Su", $"fi.{Guid.NewGuid():N}@example.com", null, null, Now);
            db.RecruitmentStages.AddRange(stages);
            db.Vacancies.Add(vacancy);
            db.Candidates.Add(cand);
            db.Applications.Add(Application.Create(Guid.NewGuid(), otherCompanyId, vacancy.Id, cand.Id, cv.Id, null, Now));
            await db.SaveChangesAsync();
        }

        var payload = await (await client.GetAsync(Url(companyId))).Content.ReadFromJsonAsync<MetricPayload>();

        Assert.NotNull(payload);
        Assert.Equal(0, payload!.Count);
        Assert.Empty(payload.Items);
    }

    private sealed record MetricPayload(int Count, List<ItemPayload> Items);
    private sealed record ItemPayload(Guid ApplicationId, Guid VacancyId, Guid StageId);
}
