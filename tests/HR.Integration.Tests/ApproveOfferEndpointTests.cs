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

/// <summary>
/// See ApproveOfferHandlerTests/ApplicationTests in HR.Modules.Recruitment.Tests for the equivalent
/// unit-level coverage of the same behaviour.
/// </summary>
[Collection("Integration")]
public class ApproveOfferEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("ce000032-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public ApproveOfferEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
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

    private async Task<(Guid VacancyId, Guid ApplicationId)> SeedApplicationAsync(Guid companyId, bool withdrawn = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageSeeder.BuildDefaultStages(companyId, Now);
        var interviewStageId = stages.Single(s => s.Name == "Interview").Id;
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, interviewStageId, null, Now);
        if (withdrawn)
            application.Withdraw(Now);
        db.RecruitmentStages.AddRange(stages);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();
        return (vacancy.Id, application.Id);
    }

    [Fact]
    public async Task Post_ApproveOffer_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}/applications/{Guid.NewGuid()}/approve-offer", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ApproveOffer_Approves_Successfully()
    {
        var companyId = Guid.NewGuid();
        var (vacancyId, applicationId) = await SeedApplicationAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/approve-offer", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApproveOfferPayload>();
        Assert.NotNull(payload);
        Assert.Equal(applicationId, payload!.ApplicationId);
        Assert.NotNull(payload.OfferApprovedAt);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.Applications.SingleAsync(a => a.Id == applicationId);
        Assert.NotNull(saved.OfferApprovedAt);
    }

    [Fact]
    public async Task Post_ApproveOffer_Returns_NotFound_For_Unknown_Application()
    {
        var companyId = Guid.NewGuid();
        var (vacancyId, _) = await SeedApplicationAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{Guid.NewGuid()}/approve-offer", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ApproveOffer_Returns_BadRequest_For_Withdrawn_Application()
    {
        var companyId = Guid.NewGuid();
        var (vacancyId, applicationId) = await SeedApplicationAsync(companyId, withdrawn: true);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/approve-offer", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record ApproveOfferPayload(Guid ApplicationId, Guid CompanyId, DateTimeOffset? OfferApprovedAt, Guid? OfferApprovedByUserId);
}
