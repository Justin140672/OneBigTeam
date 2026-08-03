using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetExternalRecruiterActivitySummaryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cd000010-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetExternalRecruiterActivitySummaryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<Guid> SeedRecruiterAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();
        return recruiter.Id;
    }

    [Fact]
    public async Task Get_ActivitySummary_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/external-recruiters/{Guid.NewGuid()}/activity-summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ActivitySummary_Returns_NotFound_When_Recruiter_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/external-recruiters/{Guid.NewGuid()}/activity-summary");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_ActivitySummary_Returns_Empty_Summary_For_Recruiter_With_No_Activity()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var recruiterId = await SeedRecruiterAsync(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/external-recruiters/{recruiterId}/activity-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.CurrentVacancies);
        Assert.Empty(payload.PreviousVacancies);
        Assert.Equal(0, payload.CandidatesIntroducedCount);
        Assert.Equal(0, payload.CandidatesHiredCount);
    }

    [Fact]
    public async Task Get_ActivitySummary_Reflects_Current_Vacancy_Assignment()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var recruiterId = await SeedRecruiterAsync(companyId);

        Guid vacancyId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now, recruiterId);
            db.Vacancies.Add(vacancy);
            await db.SaveChangesAsync();
            vacancyId = vacancy.Id;
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/external-recruiters/{recruiterId}/activity-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(payload);
        Assert.Single(payload!.CurrentVacancies);
        Assert.Equal(vacancyId, payload.CurrentVacancies[0].VacancyId);
    }

    [Fact]
    public async Task Get_ActivitySummary_Excludes_Vacancy_Reassigned_Away_Before_Closing()
    {
        // End-to-end confirmation of the ticket #81 behaviour change: once a vacancy's
        // AssignedRecruiterId is repointed away from a recruiter before the vacancy reaches a
        // terminal status, that recruiter no longer appears in either bucket for that vacancy.
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var recruiterId = await SeedRecruiterAsync(companyId);

        Guid replacementRecruiterId;
        Guid vacancyId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var replacementRecruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Beta Talent", null, null, null, null, null, Now);
            db.ExternalRecruiters.Add(replacementRecruiter);
            replacementRecruiterId = replacementRecruiter.Id;

            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now, recruiterId);
            vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
            vacancy.AssignRecruiter(replacementRecruiterId, Now);
            vacancy.Close(Now, DateOnly.FromDateTime(Now.UtcDateTime));
            db.Vacancies.Add(vacancy);
            await db.SaveChangesAsync();
            vacancyId = vacancy.Id;
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/external-recruiters/{recruiterId}/activity-summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.CurrentVacancies);
        Assert.Empty(payload.PreviousVacancies);

        var replacementResponse = await client.GetAsync($"/api/companies/{companyId}/external-recruiters/{replacementRecruiterId}/activity-summary");
        Assert.Equal(HttpStatusCode.OK, replacementResponse.StatusCode);
        var replacementPayload = await replacementResponse.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(replacementPayload);
        Assert.Empty(replacementPayload!.CurrentVacancies);
        Assert.Single(replacementPayload.PreviousVacancies);
        Assert.Equal(vacancyId, replacementPayload.PreviousVacancies[0].VacancyId);
    }

    private sealed record VacancyActivityItemPayload(Guid VacancyId, string? AdvertTitle);

    private sealed record SummaryPayload(
        Guid ExternalRecruiterId,
        string AgencyName,
        IReadOnlyList<VacancyActivityItemPayload> CurrentVacancies,
        IReadOnlyList<VacancyActivityItemPayload> PreviousVacancies,
        int CandidatesIntroducedCount,
        int CandidatesHiredCount);
}
