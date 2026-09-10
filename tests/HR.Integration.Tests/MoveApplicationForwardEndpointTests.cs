using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Postgres integration coverage for POST
/// /api/companies/{c}/vacancies/{v}/applications/{a}/move-forward (Ticket 1 — "Move Forward").
/// Unit-level equivalents live in MoveApplicationForwardHandlerTests / MoveApplicationForwardValidatorTests.
/// </summary>
[Collection("Integration")]
public class MoveApplicationForwardEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc000f1d-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc000f1d-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public MoveApplicationForwardEndpointTests(ApiWebApplicationFactory factory)
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

    private static string Url(Guid companyId, Guid vacancyId, Guid applicationId) =>
        $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/move-forward";

    private async Task<Guid> InterviewStageId(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        return await db.RecruitmentStages
            .Where(s => s.CompanyId == companyId && s.Name == "Interview")
            .Select(s => s.Id)
            .SingleAsync();
    }

    private async Task DeactivateStagesAfterCvReviewAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var stages = await db.RecruitmentStages
            .Where(s => s.CompanyId == companyId && (s.Name == "Interview" || s.Name == "Offer"))
            .ToListAsync();
        foreach (var stage in stages)
            stage.SetActiveStatus(false, Now);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Post_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            Url(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_Forbidden_For_View_Only_User()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.PostAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId),
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Advances_To_Next_Default_Stage_And_Creates_History_Row()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var interviewStageId = await InterviewStageId(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId),
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, cvReviewNotes = "Advancing" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Payload>();
        Assert.NotNull(payload);
        Assert.Equal(seeded.CvReviewStageId, payload!.PreviousStageId);
        Assert.Equal(interviewStageId, payload.CurrentStageId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.Applications.SingleAsync(a => a.Id == seeded.ApplicationId);
        Assert.Equal(interviewStageId, saved.CurrentStageId);
        Assert.Equal("Advancing", saved.CvReviewNotes);
        var history = await db.ApplicationStageHistoryEntries.Where(e => e.ApplicationId == seeded.ApplicationId).ToListAsync();
        Assert.Single(history);
    }

    [Fact]
    public async Task Post_Returns_BadRequest_When_Application_On_Terminal_Stage()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        await RecruitmentTestSeeder.MarkApplicationOnStageAsync(_factory, seeded.ApplicationId, seeded.HiredStageId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId),
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_BadRequest_When_No_Next_Active_Stage()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        await DeactivateStagesAfterCvReviewAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId),
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.Applications.SingleAsync(a => a.Id == seeded.ApplicationId);
        Assert.Equal(seeded.CvReviewStageId, saved.CurrentStageId);
    }

    [Fact]
    public async Task Post_Returns_NotFound_For_Unknown_Application()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            Url(companyId, seeded.VacancyId, Guid.NewGuid()),
            new { companyId, vacancyId = seeded.VacancyId, applicationId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_NotFound_For_Application_In_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyA, Now);
        using var clientB = await ClientAs(RecruiterUser, companyB);

        var response = await clientB.PostAsJsonAsync(
            Url(companyB, seeded.VacancyId, seeded.ApplicationId),
            new { companyId = companyB, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record Payload(
        Guid Id, Guid VacancyId, Guid CandidateId, Guid PreviousStageId, Guid CurrentStageId,
        string CurrentStageName, string? CvReviewNotes, DateTimeOffset UpdatedAt);
}
