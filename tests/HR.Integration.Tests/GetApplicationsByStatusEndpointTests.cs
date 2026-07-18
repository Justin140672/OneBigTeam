using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class GetApplicationsByStatusEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc000006-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc000006-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetApplicationsByStatusEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_ApplicationsByStatus_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/recruitment/applications?status=Applied");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ApplicationsByStatus_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/recruitment/applications?status=Applied");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("Withdrawn")]
    public async Task Get_ApplicationsByStatus_Returns_UnprocessableEntity_For_Excluded_Status(string status)
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/recruitment/applications?status={status}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_ApplicationsByStatus_Returns_Empty_List_When_No_Matches()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/recruitment/applications?status=Applied");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_ApplicationsByStatus_Returns_Matching_Applications_With_Candidate_And_Vacancy_Details()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        Guid applicationId = Guid.Empty, candidateId = Guid.Empty, vacancyId = Guid.Empty;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
            var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, Now);
            var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);

            vacancyId = vacancy.Id;
            candidateId = candidate.Id;
            applicationId = application.Id;

            db.Vacancies.Add(vacancy);
            db.Candidates.Add(candidate);
            db.Applications.Add(application);

            // Noise: a screening application that should not be returned for status=Applied.
            var screeningCandidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", $"liam.{Guid.NewGuid():N}@example.com", null, null, Now);
            var screeningApplication = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, screeningCandidate.Id, null, Now);
            screeningApplication.MoveToScreening(Now);
            db.Candidates.Add(screeningCandidate);
            db.Applications.Add(screeningApplication);

            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/recruitment/applications?status=Applied");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(applicationId, item.ApplicationId);
        Assert.Equal(candidateId, item.CandidateId);
        Assert.Equal("Emma Clarke", item.CandidateName);
        Assert.Equal(vacancyId, item.VacancyId);
        Assert.Equal("Senior Software Engineer", item.VacancyTitle);
    }

    [Fact]
    public async Task Get_ApplicationsByStatus_Isolates_By_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), otherCompanyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
            var candidate = Candidate.Create(Guid.NewGuid(), otherCompanyId, "Nina", "Patel", $"nina.{Guid.NewGuid():N}@example.com", null, null, Now);
            var application = Application.Create(Guid.NewGuid(), otherCompanyId, vacancy.Id, candidate.Id, null, Now);
            db.Vacancies.Add(vacancy);
            db.Candidates.Add(candidate);
            db.Applications.Add(application);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/recruitment/applications?status=Applied");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record ListPayload(List<ApplicationItem> Items);

    private sealed record ApplicationItem(
        Guid ApplicationId,
        Guid CandidateId,
        string CandidateName,
        string CandidateEmail,
        Guid VacancyId,
        string VacancyTitle,
        DateTimeOffset AppliedAt);
}
