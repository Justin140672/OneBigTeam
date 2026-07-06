using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Covers the two endpoints backing the Recruitment dashboard widget:
/// GetInterviewsTodayCount (Recruitment module) and GetOutstandingTaskCount (Tasks module).
/// </summary>
public class RecruitmentDashboardSummaryEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid HrAdminUserId  = new("dd000001-0000-0000-0000-000000000001");
    private static readonly Guid EmployeeUserId = new("dd000001-0000-0000-0000-000000000002");

    public RecruitmentDashboardSummaryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private HttpClient ClientAs(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<(Guid VacancyId, Guid CandidateId, Guid ApplicationId)> SeedApplicationAsync(HttpClient client, Guid companyId)
    {
        var vacancyResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            title = "Senior Software Engineer",
            hiringManagerId = Guid.NewGuid()
        });
        vacancyResponse.EnsureSuccessStatusCode();
        var vacancy = await vacancyResponse.Content.ReadFromJsonAsync<VacancyPayload>();

        var candidateResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/candidates", new
        {
            companyId,
            firstName = "Emma",
            lastName = "Clarke",
            email = $"emma.clarke.{Guid.NewGuid():N}@example.com"
        });
        candidateResponse.EnsureSuccessStatusCode();
        var candidate = await candidateResponse.Content.ReadFromJsonAsync<CandidatePayload>();

        var applicationResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancy!.Id}/applications", new
            {
                companyId,
                vacancyId = vacancy.Id,
                candidateId = candidate!.Id
            });
        applicationResponse.EnsureSuccessStatusCode();
        var application = await applicationResponse.Content.ReadFromJsonAsync<ApplicationPayload>();

        return (vacancy.Id, candidate.Id, application!.Id);
    }

    // ── GetInterviewsTodayCount ──────────────────────────────────────────────────

    [Fact]
    public async Task Get_InterviewsTodayCount_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/interviews/today-count");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_InterviewsTodayCount_Returns_Zero_When_No_Interviews_Scheduled()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(HrAdminUserId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/interviews/today-count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CountPayload>();
        Assert.Equal(0, payload!.Count);
    }

    [Fact]
    public async Task Get_InterviewsTodayCount_Counts_Interview_Scheduled_For_Today()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(HrAdminUserId, companyId);
        var (vacancyId, _, applicationId) = await SeedApplicationAsync(client, companyId);

        var scheduleResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/interviews", new
            {
                companyId,
                vacancyId,
                applicationId,
                interviewerEmployeeId = Guid.NewGuid(),
                scheduledAt = DateTimeOffset.UtcNow.AddHours(2),
                durationMinutes = 30
            });
        scheduleResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/interviews/today-count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CountPayload>();
        Assert.Equal(1, payload!.Count);
    }

    [Fact]
    public async Task Get_InterviewsTodayCount_Excludes_Interviews_For_Other_Companies()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = ClientAs(HrAdminUserId, companyId);
        using var otherClient = ClientAs(HrAdminUserId, otherCompanyId);
        var (vacancyId, _, applicationId) = await SeedApplicationAsync(otherClient, otherCompanyId);

        var scheduleResponse = await otherClient.PostAsJsonAsync(
            $"/api/companies/{otherCompanyId}/vacancies/{vacancyId}/applications/{applicationId}/interviews", new
            {
                companyId = otherCompanyId,
                vacancyId,
                applicationId,
                interviewerEmployeeId = Guid.NewGuid(),
                scheduledAt = DateTimeOffset.UtcNow.AddHours(2),
                durationMinutes = 30
            });
        scheduleResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/interviews/today-count");

        var payload = await response.Content.ReadFromJsonAsync<CountPayload>();
        Assert.Equal(0, payload!.Count);
    }

    // ── GetOutstandingTaskCount ───────────────────────────────────────────────────

    [Fact]
    public async Task Get_OutstandingTaskCount_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/tasks/outstanding-count");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_OutstandingTaskCount_Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(EmployeeUserId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/tasks/outstanding-count");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_OutstandingTaskCount_Counts_Interview_Feedback_Task_After_Scheduling()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(HrAdminUserId, companyId);
        var (vacancyId, _, applicationId) = await SeedApplicationAsync(client, companyId);

        var scheduleResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/interviews", new
            {
                companyId,
                vacancyId,
                applicationId,
                interviewerEmployeeId = Guid.NewGuid(),
                scheduledAt = DateTimeOffset.UtcNow.AddDays(3),
                durationMinutes = 30
            });
        scheduleResponse.EnsureSuccessStatusCode();

        // Scheduling an interview creates two tasks: a "Review" prep task and a "Complete"
        // feedback task. Only the feedback task should be counted here.
        var response = await client.GetAsync(
            $"/api/companies/{companyId}/tasks/outstanding-count?source=Recruitment&actionType=Complete");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CountPayload>();
        Assert.Equal(1, payload!.Count);
    }

    [Fact]
    public async Task Get_OutstandingTaskCount_Returns_Zero_When_No_Matching_Tasks()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(HrAdminUserId, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/tasks/outstanding-count?source=Recruitment&actionType=Complete");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CountPayload>();
        Assert.Equal(0, payload!.Count);
    }

    private sealed record CountPayload(int Count);
    private sealed record VacancyPayload(Guid Id);
    private sealed record CandidatePayload(Guid Id);
    private sealed record ApplicationPayload(Guid Id);
}
