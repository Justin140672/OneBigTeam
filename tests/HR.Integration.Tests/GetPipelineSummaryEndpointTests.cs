using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class GetPipelineSummaryEndpointTests : IClassFixture<ApiWebApplicationFactory>
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
    public async Task Get_PipelineSummary_Returns_All_Six_Stages_Zero_Filled_When_No_Applications()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment/pipeline-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(payload);
        Assert.Equal(
            ["Applied", "Screening", "InterviewScheduled", "Interviewed", "Offered", "Hired"],
            payload!.Items.Select(i => i.Status).ToArray());
        Assert.All(payload.Items, i => Assert.Equal(0, i.ApplicationCount));
    }

    [Fact]
    public async Task Get_PipelineSummary_Groups_By_Status_And_Excludes_Rejected_And_Withdrawn()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        await SeedAsync(scope =>
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Software Engineer", null, Guid.NewGuid(), Now);
            db.Vacancies.Add(vacancy);

            for (var i = 0; i < 4; i++)
            {
                var candidate = Candidate.Create(
                    Guid.NewGuid(), companyId, "First", $"Last{i}", $"c{i}.{Guid.NewGuid():N}@example.com", null, null, Now);
                db.Candidates.Add(candidate);

                var status = i switch
                {
                    0 => ApplicationStatus.Applied,
                    1 => ApplicationStatus.Applied,
                    2 => ApplicationStatus.Rejected,
                    _ => ApplicationStatus.Withdrawn,
                };
                db.Applications.Add(CreateApplicationWithStatus(companyId, vacancy.Id, candidate.Id, status));
            }
        });

        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment/pipeline-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(payload);
        Assert.Equal(6, payload!.Items.Count);
        Assert.DoesNotContain(payload.Items, i => i.Status is "Rejected" or "Withdrawn");

        var applied = Assert.Single(payload.Items, i => i.Status == "Applied");
        Assert.Equal(2, applied.ApplicationCount);
        Assert.Equal(2, payload.Items.Sum(i => i.ApplicationCount));
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
            var otherVacancy = Vacancy.Create(Guid.NewGuid(), otherCompanyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
            var otherCandidate = Candidate.Create(Guid.NewGuid(), otherCompanyId, "First", "Last", $"c.{Guid.NewGuid():N}@example.com", null, null, Now);
            db.Vacancies.Add(otherVacancy);
            db.Candidates.Add(otherCandidate);
            db.Applications.Add(CreateApplicationWithStatus(otherCompanyId, otherVacancy.Id, otherCandidate.Id, ApplicationStatus.Applied));
        });

        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment/pipeline-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(payload);
        Assert.Equal(0, payload!.Items.Sum(i => i.ApplicationCount));
    }

    private static Application CreateApplicationWithStatus(Guid companyId, Guid vacancyId, Guid candidateId, ApplicationStatus status)
    {
        var application = Application.Create(Guid.NewGuid(), companyId, vacancyId, candidateId, null, Now);

        switch (status)
        {
            case ApplicationStatus.Applied:
                break;
            case ApplicationStatus.Rejected:
                application.Reject(Now);
                break;
            case ApplicationStatus.Withdrawn:
                application.Withdraw(Now);
                break;
        }

        return application;
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
