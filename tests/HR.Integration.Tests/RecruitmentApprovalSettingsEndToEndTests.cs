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
/// SET-05 end-to-end: proves that flipping the VacancyApprovalRequired/OfferApprovalRequired company
/// settings actually changes PublishVacancy/OfferCandidate behaviour through the real HTTP pipeline —
/// not just at the unit-test level (see PublishVacancyHandlerTests/OfferCandidateHandlerTests).
/// </summary>
[Collection("Integration")]
public class RecruitmentApprovalSettingsEndToEndTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUser = new("ce000034-0000-0000-0000-000000000001");
    private static readonly Guid RecruiterUser = new("ce000034-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public RecruitmentApprovalSettingsEndToEndTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.Employee);
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

    private async Task<Guid> SeedDraftVacancyAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return vacancy.Id;
    }

    private async Task<(Guid VacancyId, Guid ApplicationId)> SeedOpenVacancyWithApplicationAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        var stages = RecruitmentStageSeeder.BuildDefaultStages(companyId, Now);
        var interviewStageId = stages.Single(s => s.Name == "Interview").Id;
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, interviewStageId, null, Now);
        db.RecruitmentStages.AddRange(stages);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();
        return (vacancy.Id, application.Id);
    }

    [Fact]
    public async Task VacancyApprovalRequired_Setting_Blocks_And_Then_Allows_Publish_After_Approval()
    {
        var companyId = Guid.NewGuid();
        using var adminClient = await ClientAs(HrAdminUser, companyId);
        var vacancyId = await SeedDraftVacancyAsync(companyId);

        var settingsResponse = await adminClient.PutAsJsonAsync($"/api/companies/{companyId}/recruitment-settings", new
        {
            vacancyApprovalRequired = true,
            offerApprovalRequired = false,
            candidateRetentionDays = 730,
            version = 1,
        });
        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);

        using var recruiterClient = await ClientAs(RecruiterUser, companyId);

        // Publish rejected: vacancy has not been approved yet.
        var firstPublishResponse = await recruiterClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/publish", new { });
        Assert.Equal(HttpStatusCode.BadRequest, firstPublishResponse.StatusCode);

        // Approve the vacancy.
        var approveResponse = await recruiterClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        // Publish now succeeds.
        var secondPublishResponse = await recruiterClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/publish", new { });
        Assert.Equal(HttpStatusCode.OK, secondPublishResponse.StatusCode);
    }

    [Fact]
    public async Task OfferApprovalRequired_Setting_Blocks_And_Then_Allows_Offer_After_Approval()
    {
        var companyId = Guid.NewGuid();
        using var adminClient = await ClientAs(HrAdminUser, companyId);
        var (vacancyId, applicationId) = await SeedOpenVacancyWithApplicationAsync(companyId);

        var settingsResponse = await adminClient.PutAsJsonAsync($"/api/companies/{companyId}/recruitment-settings", new
        {
            vacancyApprovalRequired = false,
            offerApprovalRequired = true,
            candidateRetentionDays = 730,
            version = 1,
        });
        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);

        using var recruiterClient = await ClientAs(RecruiterUser, companyId);

        // Offer rejected: application's offer has not been approved yet.
        var firstOfferResponse = await recruiterClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer", new { });
        Assert.Equal(HttpStatusCode.BadRequest, firstOfferResponse.StatusCode);

        // Approve the offer.
        var approveOfferResponse = await recruiterClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/approve-offer", new { });
        Assert.Equal(HttpStatusCode.OK, approveOfferResponse.StatusCode);

        // Offer now succeeds.
        var secondOfferResponse = await recruiterClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer", new { });
        Assert.Equal(HttpStatusCode.OK, secondOfferResponse.StatusCode);
    }
}
