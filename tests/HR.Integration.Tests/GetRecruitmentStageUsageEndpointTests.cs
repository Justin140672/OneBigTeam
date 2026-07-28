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

public class GetRecruitmentStageUsageEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000a3-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetRecruitmentStageUsageEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
        }).GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<Dictionary<string, Guid>> SeedDefaultStagesAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var stages = RecruitmentStageSeeder.BuildDefaultStages(companyId, Now);
        db.RecruitmentStages.AddRange(stages);
        await db.SaveChangesAsync();
        return stages.ToDictionary(s => s.Name, s => s.Id);
    }

    [Fact]
    public async Task Get_Usage_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/recruitment-stages/{Guid.NewGuid()}/usage");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Usage_Returns_NotFound_For_Unknown_Stage()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/recruitment-stages/{Guid.NewGuid()}/usage");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Usage_Returns_Not_InUse_When_No_Applications_On_Stage()
    {
        var companyId = Guid.NewGuid();
        var stages = await SeedDefaultStagesAsync(companyId);
        using var client = AuthenticatedClient(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/recruitment-stages/{stages["Offer"]}/usage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UsagePayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.InUse);
        Assert.Equal(0, payload.ActiveVacancyCount);
    }

    [Fact]
    public async Task Get_Usage_Returns_InUse_When_Application_On_Active_Vacancy_Is_On_Stage()
    {
        var companyId = Guid.NewGuid();
        var stages = await SeedDefaultStagesAsync(companyId);
        using var client = AuthenticatedClient(companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
            var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
            var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages["Offer"], null, Now);
            db.Vacancies.Add(vacancy);
            db.Candidates.Add(candidate);
            db.Applications.Add(application);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/recruitment-stages/{stages["Offer"]}/usage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UsagePayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.InUse);
        Assert.Equal(1, payload.ActiveVacancyCount);
        Assert.Single(payload.VacancyLabels);
    }

    private sealed record UsagePayload(
        Guid RecruitmentStageId,
        bool InUse,
        int ActiveVacancyCount,
        IReadOnlyList<string> VacancyLabels);
}
