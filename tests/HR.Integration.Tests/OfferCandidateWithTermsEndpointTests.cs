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
/// Ticket 2: the OfferCandidate (POST .../offer) endpoint now accepts optional offer terms
/// (salary/frequency/proposed start date/offer date/notes) and records them on the Application,
/// putting the offer into AwaitingResponse. Unit coverage lives in OfferCandidateHandlerTests /
/// OfferCandidateValidatorTests; this proves it end-to-end over real HTTP + auth + persistence.
/// </summary>
[Collection("Integration")]
public class OfferCandidateWithTermsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("ce000041-0000-0000-0000-000000000001");
    private static readonly Guid HrAdminUser = new("ce000041-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public OfferCandidateWithTermsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.Employee);
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

    private async Task<(Guid VacancyId, Guid ApplicationId)> SeedInterviewStageApplicationAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
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

    private static object TermsBody(Guid companyId, Guid vacancyId, Guid applicationId) => new
    {
        companyId,
        vacancyId,
        applicationId,
        offeredSalary = 68000m,
        offeredSalaryFrequency = "annual",
        proposedStartDate = new DateOnly(2026, 10, 1).ToString("yyyy-MM-dd"),
        offerDate = new DateOnly(2026, 7, 4).ToString("yyyy-MM-dd"),
        offerNotes = "Top of band, car allowance included.",
    };

    [Fact]
    public async Task Post_Offer_With_Terms_Returns_Ok_And_Records_Terms()
    {
        var companyId = Guid.NewGuid();
        var (vacancyId, applicationId) = await SeedInterviewStageApplicationAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer",
            TermsBody(companyId, vacancyId, applicationId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OfferPayload>();
        Assert.NotNull(payload);
        Assert.Equal(68000m, payload!.OfferedSalary);
        Assert.Equal("Annual", payload.OfferedSalaryFrequency);
        Assert.Equal(new DateOnly(2026, 10, 1), payload.ProposedStartDate);
        Assert.Equal(new DateOnly(2026, 7, 4), payload.OfferDate);
        Assert.Equal("Top of band, car allowance included.", payload.OfferNotes);
        Assert.Equal("AwaitingResponse", payload.OfferResponseStatus);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.Applications.SingleAsync(a => a.Id == applicationId);
        Assert.Equal(68000m, saved.OfferedSalary);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, saved.OfferResponseStatus);
        Assert.NotNull(saved.OfferMadeAt);
    }

    [Fact]
    public async Task Post_Offer_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}/applications/{Guid.NewGuid()}/offer", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Offer_Returns_NotFound_For_Unknown_Application()
    {
        var companyId = Guid.NewGuid();
        var (vacancyId, _) = await SeedInterviewStageApplicationAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{Guid.NewGuid()}/offer", new
            {
                companyId,
                vacancyId,
                applicationId = Guid.NewGuid(),
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Offer_Returns_ValidationError_When_Salary_Is_Zero()
    {
        var companyId = Guid.NewGuid();
        var (vacancyId, applicationId) = await SeedInterviewStageApplicationAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer", new
            {
                companyId,
                vacancyId,
                applicationId,
                offeredSalary = 0m,
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Offer_Returns_ValidationError_For_Unrecognised_Frequency()
    {
        var companyId = Guid.NewGuid();
        var (vacancyId, applicationId) = await SeedInterviewStageApplicationAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer", new
            {
                companyId,
                vacancyId,
                applicationId,
                offeredSalaryFrequency = "Fortnightly",
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Offer_With_Terms_Is_Still_Blocked_By_OfferApprovalRequired_Until_Approved()
    {
        var companyId = Guid.NewGuid();
        var (vacancyId, applicationId) = await SeedInterviewStageApplicationAsync(companyId);

        using var adminClient = await ClientAs(HrAdminUser, companyId);
        var settingsResponse = await adminClient.PutAsJsonAsync($"/api/companies/{companyId}/recruitment-settings", new
        {
            vacancyApprovalRequired = false,
            offerApprovalRequired = true,
            candidateRetentionDays = 730,
            version = 1,
        });
        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);

        using var client = await ClientAs(RecruiterUser, companyId);

        var blocked = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer",
            TermsBody(companyId, vacancyId, applicationId));
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

        var approve = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/approve-offer", new { });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var allowed = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer",
            TermsBody(companyId, vacancyId, applicationId));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Post_Offer_By_Another_Company_Caller_Returns_NotFound()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var (vacancyId, applicationId) = await SeedInterviewStageApplicationAsync(companyA);

        using var clientB = await ClientAs(RecruiterUser, companyB);

        var response = await clientB.PostAsJsonAsync(
            $"/api/companies/{companyB}/vacancies/{vacancyId}/applications/{applicationId}/offer", new
            {
                companyId = companyB,
                vacancyId,
                applicationId,
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Offer_By_Non_Recruiter_Returns_Forbidden()
    {
        var companyId = Guid.NewGuid();
        var (vacancyId, applicationId) = await SeedInterviewStageApplicationAsync(companyId);
        using var client = await ClientAs(HrAdminUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer",
            TermsBody(companyId, vacancyId, applicationId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record OfferPayload(
        Guid Id,
        decimal? OfferedSalary,
        string? OfferedSalaryFrequency,
        DateOnly? ProposedStartDate,
        DateOnly? OfferDate,
        string? OfferNotes,
        string? OfferResponseStatus);
}
