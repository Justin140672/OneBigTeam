using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Postgres integration coverage for GET /vacancies/{v}/interviews. See
/// ListInterviewsForVacancyHandlerTests in HR.Modules.Recruitment.Tests for the unit-level
/// equivalent. Covers: anonymous 401, wrong-role 403, happy 200 with candidate join fields,
/// company isolation.
/// </summary>
[Collection("Integration")]
public class ListInterviewsForVacancyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000c8-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc0000c8-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public ListInterviewsForVacancyEndpointTests(ApiWebApplicationFactory factory)
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

    [Fact]
    public async Task Get_Interviews_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}/interviews");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Interviews_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/interviews");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Interviews_Returns_Interviews_With_Candidate_Details()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now, "Nina", "Patel");
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyId, seeded.ApplicationId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var payload = await client.GetFromJsonAsync<ListPayload>(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/interviews");

        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(interviewId, item.Id);
        Assert.Equal(seeded.ApplicationId, item.ApplicationId);
        Assert.Equal(seeded.CandidateId, item.CandidateId);
        Assert.Equal("Nina", item.CandidateFirstName);
        Assert.Equal("Pending", item.Outcome);
    }

    [Fact]
    public async Task Get_Interviews_Isolates_By_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var seededA = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyA, Now);
        await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyA, seededA.ApplicationId, Now);
        using var clientB = await ClientAs(RecruiterUser, companyB);

        var payload = await clientB.GetFromJsonAsync<ListPayload>(
            $"/api/companies/{companyB}/vacancies/{seededA.VacancyId}/interviews");

        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record ListPayload(List<Item> Items);

    private sealed record Item(
        Guid Id, Guid ApplicationId, Guid CandidateId, string CandidateFirstName, string CandidateLastName,
        Guid InterviewerEmployeeId, DateTimeOffset ScheduledAt, int? DurationMinutes, string? Location,
        string Outcome, string? Notes);
}
