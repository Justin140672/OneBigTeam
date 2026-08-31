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
public class GetOffersAwaitingResponseMetricEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("db040003-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("db040003-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetOffersAwaitingResponseMetricEndpointTests(ApiWebApplicationFactory factory)
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

    private string Url(Guid companyId) => $"/api/companies/{companyId}/recruitment/metrics/offers-awaiting-response";

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
    public async Task Returns_Zero_And_NotConfigured_When_No_Offer_Purpose_Stage()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            // Renamed pipeline, no purpose flags; last stage is deliberately a differently-purposed one.
            var s1 = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Screening", 1, false, RecruitmentStageTerminalOutcome.None, Now);
            var s2 = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Final panel", 2, false, RecruitmentStageTerminalOutcome.None, Now);
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "PM", null, Guid.NewGuid(), Now);
            var cand = Candidate.Create(Guid.NewGuid(), companyId, "Ana", "Ng", $"ana.{Guid.NewGuid():N}@example.com", null, null, Now);
            db.RecruitmentStages.AddRange(s1, s2);
            db.Vacancies.Add(vacancy);
            db.Candidates.Add(cand);
            db.Applications.Add(Application.Create(Guid.NewGuid(), companyId, vacancy.Id, cand.Id, s2.Id, null, Now));
            await db.SaveChangesAsync();
        }

        var payload = await (await client.GetAsync(Url(companyId))).Content.ReadFromJsonAsync<MetricPayload>();

        Assert.NotNull(payload);
        Assert.False(payload!.OfferStageConfigured);
        Assert.Equal(0, payload.Count);
        Assert.Empty(payload.Items);
    }

    [Fact]
    public async Task Sums_Across_Multiple_Offer_Purpose_Stages_And_Count_Matches_Items()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var verbal = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Verbal offer", 4, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Offer);
            var written = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Written offer", 5, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Offer);
            var interview = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Interview", 3, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "PM", null, Guid.NewGuid(), Now);
            var c1 = Candidate.Create(Guid.NewGuid(), companyId, "Ana", "Ng", $"ana.{Guid.NewGuid():N}@example.com", null, null, Now);
            var c2 = Candidate.Create(Guid.NewGuid(), companyId, "Bo", "Li", $"bo.{Guid.NewGuid():N}@example.com", null, null, Now);
            var c3 = Candidate.Create(Guid.NewGuid(), companyId, "Cy", "Fox", $"cy.{Guid.NewGuid():N}@example.com", null, null, Now);
            var c4 = Candidate.Create(Guid.NewGuid(), companyId, "Di", "Ma", $"di.{Guid.NewGuid():N}@example.com", null, null, Now);
            var withdrawn = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, c3.Id, written.Id, null, Now);
            withdrawn.Withdraw(Now);
            db.RecruitmentStages.AddRange(verbal, written, interview);
            db.Vacancies.Add(vacancy);
            db.Candidates.AddRange(c1, c2, c3, c4);
            db.Applications.AddRange(
                Application.Create(Guid.NewGuid(), companyId, vacancy.Id, c1.Id, verbal.Id, null, Now),
                Application.Create(Guid.NewGuid(), companyId, vacancy.Id, c2.Id, written.Id, null, Now),
                withdrawn,
                Application.Create(Guid.NewGuid(), companyId, vacancy.Id, c4.Id, interview.Id, null, Now));
            await db.SaveChangesAsync();
        }

        var payload = await (await client.GetAsync(Url(companyId))).Content.ReadFromJsonAsync<MetricPayload>();

        Assert.NotNull(payload);
        Assert.True(payload!.OfferStageConfigured);
        Assert.Equal(2, payload.Count);
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
            var offer = stages.Single(s => s.Name == "Offer");
            var vacancy = Vacancy.Create(Guid.NewGuid(), otherCompanyId, Guid.NewGuid(), "PM", null, Guid.NewGuid(), Now);
            var cand = Candidate.Create(Guid.NewGuid(), otherCompanyId, "Fi", "Su", $"fi.{Guid.NewGuid():N}@example.com", null, null, Now);
            db.RecruitmentStages.AddRange(stages);
            db.Vacancies.Add(vacancy);
            db.Candidates.Add(cand);
            db.Applications.Add(Application.Create(Guid.NewGuid(), otherCompanyId, vacancy.Id, cand.Id, offer.Id, null, Now));
            await db.SaveChangesAsync();
        }

        var payload = await (await client.GetAsync(Url(companyId))).Content.ReadFromJsonAsync<MetricPayload>();

        Assert.NotNull(payload);
        Assert.Equal(0, payload!.Count);
    }

    private sealed record MetricPayload(int Count, bool OfferStageConfigured, List<ItemPayload> Items);
    private sealed record ItemPayload(Guid ApplicationId, Guid VacancyId, Guid StageId);
}
