using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Postgres integration coverage for GET /vacancies/{v}/applications. See
/// ListApplicationsForVacancyHandlerTests in HR.Modules.Recruitment.Tests for the unit-level
/// equivalent. Covers: anonymous 401, wrong-role 403, happy 200 with candidate join fields,
/// stage filter, and company isolation (a different company's applications never leak in).
/// </summary>
[Collection("Integration")]
public class ListApplicationsForVacancyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000c7-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc0000c7-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public ListApplicationsForVacancyEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_Applications_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}/applications");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Applications_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Applications_Returns_Applications_With_Candidate_Details()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now, "Nina", "Patel");
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(seeded.ApplicationId, item.Id);
        Assert.Equal(seeded.CandidateId, item.CandidateId);
        Assert.Equal("Nina", item.CandidateFirstName);
        Assert.Equal("Patel", item.CandidateLastName);
        Assert.False(item.IsWithdrawn);
    }

    [Fact]
    public async Task Get_Applications_Filters_By_StageId()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var matching = await client.GetFromJsonAsync<ListPayload>(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications?stageId={seeded.CvReviewStageId}");
        Assert.NotNull(matching);
        Assert.Single(matching!.Items);

        var other = await client.GetFromJsonAsync<ListPayload>(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications?stageId={seeded.HiredStageId}");
        Assert.NotNull(other);
        Assert.Empty(other!.Items);
    }

    [Fact]
    public async Task Get_Applications_Isolates_By_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var seededA = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyA, Now);
        using var clientB = await ClientAs(RecruiterUser, companyB);

        // Same vacancy id, but requested under company B's tenant/route — no rows belong to B.
        var payload = await clientB.GetFromJsonAsync<ListPayload>(
            $"/api/companies/{companyB}/vacancies/{seededA.VacancyId}/applications");

        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record ListPayload(List<Item> Items);

    private sealed record Item(
        Guid Id, Guid CandidateId, string CandidateFirstName, string CandidateLastName, string CandidateEmail,
        Guid CurrentStageId, string? InterviewOutcome, bool IsWithdrawn, DateTimeOffset AppliedAt);
}
