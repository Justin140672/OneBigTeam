using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Postgres integration coverage for PUT
/// /api/companies/{c}/vacancies/{v}/applications/{a}/cv-review-notes (Ticket 1 — "Save Notes").
/// Unit-level equivalents live in SaveCvReviewNotesHandlerTests / SaveCvReviewNotesValidatorTests.
/// </summary>
[Collection("Integration")]
public class SaveCvReviewNotesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc00cf1d-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc00cf1d-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public SaveCvReviewNotesEndpointTests(ApiWebApplicationFactory factory)
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
        $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/cv-review-notes";

    [Fact]
    public async Task Put_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            Url(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), new { cvReviewNotes = "hi" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns_Forbidden_For_View_Only_User()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.PutAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId),
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, cvReviewNotes = "hi" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Persists_Notes_And_Leaves_Stage_Unchanged()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId),
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, cvReviewNotes = "  Strong candidate  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Payload>();
        Assert.NotNull(payload);
        Assert.Equal("Strong candidate", payload!.CvReviewNotes);
        Assert.Equal(seeded.CvReviewStageId, payload.CurrentStageId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.Applications.SingleAsync(a => a.Id == seeded.ApplicationId);
        Assert.Equal("Strong candidate", saved.CvReviewNotes);
        Assert.Equal(seeded.CvReviewStageId, saved.CurrentStageId);
        Assert.NotNull(saved.CvReviewedAt);
        Assert.Equal(RecruiterUser, saved.CvReviewedByUserId);
        Assert.Empty(await db.ApplicationStageHistoryEntries.Where(e => e.ApplicationId == seeded.ApplicationId).ToListAsync());
    }

    [Fact]
    public async Task Put_Returns_NotFound_For_Unknown_Application()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            Url(companyId, seeded.VacancyId, Guid.NewGuid()),
            new { companyId, vacancyId = seeded.VacancyId, applicationId = Guid.NewGuid(), cvReviewNotes = "hi" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns_NotFound_For_Application_In_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyA, Now);
        using var clientB = await ClientAs(RecruiterUser, companyB);

        var response = await clientB.PutAsJsonAsync(
            Url(companyB, seeded.VacancyId, seeded.ApplicationId),
            new { companyId = companyB, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, cvReviewNotes = "hi" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns_BadRequest_When_Application_Withdrawn()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        await RecruitmentTestSeeder.WithdrawApplicationAsync(_factory, seeded.ApplicationId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId),
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, cvReviewNotes = "hi" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns_Validation_Error_When_Notes_Too_Long()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId),
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, cvReviewNotes = new string('A', 4001) });

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
            $"Expected 400 or 422 but got {(int)response.StatusCode}.");
    }

    private sealed record Payload(
        Guid Id, Guid VacancyId, Guid CandidateId, Guid CurrentStageId, string? CvReviewNotes,
        DateTimeOffset? CvReviewedAt, Guid? CvReviewedByUserId, DateTimeOffset UpdatedAt);
}
