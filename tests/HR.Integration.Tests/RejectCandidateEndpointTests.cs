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
/// Postgres integration coverage for POST /vacancies/{v}/applications/{a}/reject. See
/// RejectCandidateHandlerTests in HR.Modules.Recruitment.Tests for the unit-level equivalent.
/// Covers: anonymous 401, wrong-role 403, happy 200 + moved to Rejected terminal stage,
/// unknown application 404, cross-company 404, withdrawn 400, already-terminal 400.
/// </summary>
[Collection("Integration")]
public class RejectCandidateEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000c3-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc0000c3-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public RejectCandidateEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Post_Reject_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}/applications/{Guid.NewGuid()}/reject",
            new { rejectionReason = "Not a fit" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Reject_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/reject",
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, rejectionReason = "Not a fit" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Reject_Moves_Application_To_Rejected_Stage_And_Persists()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/reject",
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, rejectionReason = "Not enough experience" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RejectPayload>();
        Assert.NotNull(payload);
        Assert.Equal(seeded.RejectedStageId, payload!.CurrentStageId);
        Assert.Equal("Not enough experience", payload.RejectionReason);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.Applications.SingleAsync(a => a.Id == seeded.ApplicationId);
        Assert.Equal(seeded.RejectedStageId, saved.CurrentStageId);
        Assert.Equal("Not enough experience", saved.RejectionReason);
    }

    [Fact]
    public async Task Post_Reject_Returns_NotFound_For_Unknown_Application()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{Guid.NewGuid()}/reject",
            new { companyId, vacancyId = seeded.VacancyId, applicationId = Guid.NewGuid(), rejectionReason = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Reject_Returns_NotFound_For_Application_In_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyA, Now);
        using var client = await ClientAs(RecruiterUser, companyB);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyB}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/reject",
            new { companyId = companyB, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, rejectionReason = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Reject_Returns_BadRequest_When_Application_Withdrawn()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        await RecruitmentTestSeeder.WithdrawApplicationAsync(_factory, seeded.ApplicationId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/reject",
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, rejectionReason = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_Reject_Returns_BadRequest_When_Application_Already_On_Terminal_Stage()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        await RecruitmentTestSeeder.MarkApplicationOnStageAsync(_factory, seeded.ApplicationId, seeded.HiredStageId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/reject",
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, rejectionReason = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record RejectPayload(
        Guid Id, Guid VacancyId, Guid CandidateId, Guid CurrentStageId, string? RejectionReason);
}
