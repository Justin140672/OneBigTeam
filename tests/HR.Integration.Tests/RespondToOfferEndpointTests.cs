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
/// Ticket 2: POST .../offer/response records the explicit response to an offer (Accepted / Declined /
/// Withdrawn). Unit coverage lives in RespondToOfferHandlerTests / RespondToOfferValidatorTests; this
/// proves it end-to-end over real HTTP + policy + persistence.
/// </summary>
[Collection("Integration")]
public class RespondToOfferEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("ce000042-0000-0000-0000-000000000001");
    private static readonly Guid HrAdminUser = new("ce000042-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public RespondToOfferEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
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

    private async Task<(Guid VacancyId, Guid ApplicationId)> SeedApplicationAsync(
        Guid companyId, bool withOffer = true, bool alreadyResolved = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageSeeder.BuildDefaultStages(companyId, Now);
        var offerStageId = stages.Single(s => s.Name == "Offer").Id;
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, offerStageId, null, Now);
        if (withOffer)
            application.RecordOfferTerms(60000m, OfferSalaryFrequency.Annual, new DateOnly(2026, 10, 1), new DateOnly(2026, 7, 4), null, Now);
        if (alreadyResolved)
            application.RespondToOffer(OfferResponseStatus.Accepted, Now);
        db.RecruitmentStages.AddRange(stages);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();
        return (vacancy.Id, application.Id);
    }

    [Fact]
    public async Task Post_Response_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}/applications/{Guid.NewGuid()}/offer/response",
            new { status = "Accepted" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Accepted", "Accepted")]
    [InlineData("declined", "Declined")]
    [InlineData("WITHDRAWN", "Withdrawn")]
    public async Task Post_Response_Records_Status_And_RespondedAt(string status, string expected)
    {
        var companyId = Guid.NewGuid();
        var (vacancyId, applicationId) = await SeedApplicationAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer/response",
            new { companyId, vacancyId, applicationId, status });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.Applications.SingleAsync(a => a.Id == applicationId);
        Assert.Equal(expected, saved.OfferResponseStatus!.Value.ToString());
        Assert.NotNull(saved.OfferRespondedAt);
    }

    [Fact]
    public async Task Post_Response_Returns_NotFound_For_Unknown_Application()
    {
        var companyId = Guid.NewGuid();
        var (vacancyId, _) = await SeedApplicationAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{Guid.NewGuid()}/offer/response",
            new { companyId, vacancyId, applicationId = Guid.NewGuid(), status = "Accepted" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Response_Returns_BadRequest_When_No_Offer_Has_Been_Made()
    {
        var companyId = Guid.NewGuid();
        var (vacancyId, applicationId) = await SeedApplicationAsync(companyId, withOffer: false);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer/response",
            new { companyId, vacancyId, applicationId, status = "Accepted" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_Response_Returns_Conflict_When_Offer_Already_Resolved()
    {
        var companyId = Guid.NewGuid();
        var (vacancyId, applicationId) = await SeedApplicationAsync(companyId, alreadyResolved: true);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer/response",
            new { companyId, vacancyId, applicationId, status = "Declined" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_Response_Returns_ValidationError_For_Invalid_Status_Value()
    {
        var companyId = Guid.NewGuid();
        var (vacancyId, applicationId) = await SeedApplicationAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer/response",
            new { companyId, vacancyId, applicationId, status = "AwaitingResponse" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Response_By_Non_Recruiter_Returns_Forbidden()
    {
        var companyId = Guid.NewGuid();
        var (vacancyId, applicationId) = await SeedApplicationAsync(companyId);
        using var client = await ClientAs(HrAdminUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer/response",
            new { companyId, vacancyId, applicationId, status = "Accepted" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Response_By_Another_Company_Caller_Returns_NotFound()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var (vacancyId, applicationId) = await SeedApplicationAsync(companyA);
        using var clientB = await ClientAs(RecruiterUser, companyB);

        var response = await clientB.PostAsJsonAsync(
            $"/api/companies/{companyB}/vacancies/{vacancyId}/applications/{applicationId}/offer/response",
            new { companyId = companyB, vacancyId, applicationId, status = "Accepted" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
