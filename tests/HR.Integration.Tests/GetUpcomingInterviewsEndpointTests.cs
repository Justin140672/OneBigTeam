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
public class GetUpcomingInterviewsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc000007-0000-0000-0000-000000000001");
    private static readonly Guid HrAdminUser = new("cc000007-0000-0000-0000-000000000002");
    private static readonly Guid PlainEmployeeUser = new("cc000007-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetUpcomingInterviewsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
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
    public async Task Get_UpcomingInterviews_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/interviews/upcoming");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_UpcomingInterviews_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/interviews/upcoming");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // candidate:view is Recruiter-only by design (see IdentityModule.AddRolePolicies) — this
    // endpoint returns CandidateName, real candidate PII, so an HR Administrator does not
    // automatically get access without also holding the Recruiter role. HrAdminUser here holds
    // only the HrAdministrator role, so Forbidden is the correct, intended result.
    [Fact]
    public async Task Get_UpcomingInterviews_Returns_Forbidden_For_HrAdministrator_Without_Recruiter_Role()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(HrAdminUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/interviews/upcoming");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_UpcomingInterviews_Returns_Empty_List_When_No_Interviews()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/interviews/upcoming");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_UpcomingInterviews_Returns_Only_Future_Pending_Interviews_With_Candidate_And_Vacancy_Details()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        Guid interviewId = Guid.Empty, candidateId = Guid.Empty, vacancyId = Guid.Empty;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
            var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, Now);
            var stages = RecruitmentStageSeeder.BuildDefaultStages(companyId, Now);
            var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages[0].Id, null, Now);
            var futureInterview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(2), 30, "Room 1", Now);

            vacancyId = vacancy.Id;
            candidateId = candidate.Id;
            interviewId = futureInterview.Id;

            db.RecruitmentStages.AddRange(stages);
            db.Vacancies.Add(vacancy);
            db.Candidates.Add(candidate);
            db.Applications.Add(application);
            db.Interviews.Add(futureInterview);

            // Noise: a past interview and a cancelled future interview should not appear.
            var pastInterview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(-2), 30, null, Now);
            var cancelledInterview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(3), 30, null, Now);
            cancelledInterview.Cancel(Now);
            db.Interviews.AddRange(pastInterview, cancelledInterview);

            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/interviews/upcoming");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(interviewId, item.InterviewId);
        Assert.Equal(candidateId, item.CandidateId);
        Assert.Equal("Emma Clarke", item.CandidateName);
        Assert.Equal(vacancyId, item.VacancyId);
        Assert.Equal("Senior Software Engineer", item.VacancyTitle);
    }

    [Fact]
    public async Task Get_UpcomingInterviews_Isolates_By_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), otherCompanyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
            var candidate = Candidate.Create(Guid.NewGuid(), otherCompanyId, "Nina", "Patel", $"nina.{Guid.NewGuid():N}@example.com", null, null, Now);
            var stages = RecruitmentStageSeeder.BuildDefaultStages(otherCompanyId, Now);
            var application = Application.Create(Guid.NewGuid(), otherCompanyId, vacancy.Id, candidate.Id, stages[0].Id, null, Now);
            db.RecruitmentStages.AddRange(stages);
            db.Vacancies.Add(vacancy);
            db.Candidates.Add(candidate);
            db.Applications.Add(application);
            db.Interviews.Add(Interview.Create(Guid.NewGuid(), otherCompanyId, application.Id, Guid.NewGuid(), Now.AddDays(2), 30, null, Now));
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/interviews/upcoming");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record ListPayload(List<InterviewItem> Items);

    private sealed record InterviewItem(
        Guid InterviewId,
        Guid ApplicationId,
        Guid CandidateId,
        string CandidateName,
        Guid VacancyId,
        string VacancyTitle,
        DateTimeOffset ScheduledAt,
        string? Location);
}
