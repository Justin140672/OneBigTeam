using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Postgres integration coverage for POST /vacancies/{v}/applications/{a}/interviews/{i}/outcome.
/// See RecordInterviewOutcomeHandlerTests / InterviewOutcomeRecorderTests in
/// HR.Modules.Recruitment.Tests for the unit-level equivalent.
/// Covers: anonymous 401, wrong-role 403, happy 200 + persisted outcome mirrored onto application,
/// unknown application 404, unknown interview 404, cross-company 404, already-recorded 400,
/// Pending outcome 422.
/// </summary>
[Collection("Integration")]
public class RecordInterviewOutcomeEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000c6-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc0000c6-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public RecordInterviewOutcomeEndpointTests(ApiWebApplicationFactory factory)
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

    private static string Url(Guid companyId, Guid vacancyId, Guid applicationId, Guid interviewId) =>
        $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/interviews/{interviewId}/outcome";

    [Fact]
    public async Task Post_Outcome_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            Url(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            new { outcome = "Passed" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Outcome_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyId, seeded.ApplicationId, Now);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.PostAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId),
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, interviewId, outcome = "Passed" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Outcome_Records_And_Mirrors_Onto_Application()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyId, seeded.ApplicationId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId),
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, interviewId, outcome = "Passed", notes = "Strong" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OutcomePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Passed", payload!.Outcome);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var savedInterview = await db.Interviews.SingleAsync(i => i.Id == interviewId);
        Assert.Equal(InterviewOutcome.Passed, savedInterview.Outcome);
        var savedApp = await db.Applications.SingleAsync(a => a.Id == seeded.ApplicationId);
        Assert.Equal(InterviewOutcome.Passed, savedApp.InterviewOutcome);
    }

    [Fact]
    public async Task Post_Outcome_Returns_NotFound_For_Unknown_Application()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyId, seeded.ApplicationId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            Url(companyId, seeded.VacancyId, Guid.NewGuid(), interviewId),
            new { companyId, vacancyId = seeded.VacancyId, applicationId = Guid.NewGuid(), interviewId, outcome = "Passed" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Outcome_Returns_NotFound_For_Unknown_Interview()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId, Guid.NewGuid()),
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, interviewId = Guid.NewGuid(), outcome = "Passed" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Outcome_Returns_NotFound_For_Interview_In_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyA, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyA, seeded.ApplicationId, Now);
        using var client = await ClientAs(RecruiterUser, companyB);

        var response = await client.PostAsJsonAsync(
            Url(companyB, seeded.VacancyId, seeded.ApplicationId, interviewId),
            new { companyId = companyB, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, interviewId, outcome = "Passed" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Outcome_Returns_BadRequest_When_Outcome_Already_Recorded()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(
            _factory, companyId, seeded.ApplicationId, Now, recordOutcome: InterviewOutcome.Failed);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId),
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, interviewId, outcome = "Passed" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_Outcome_Returns_UnprocessableEntity_When_Outcome_Is_Pending()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyId, seeded.ApplicationId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId),
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, interviewId, outcome = "Pending" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record OutcomePayload(
        Guid Id, Guid CompanyId, Guid ApplicationId, string Outcome, string? Notes);
}
