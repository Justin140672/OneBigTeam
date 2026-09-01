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
/// Postgres integration coverage for PUT /vacancies/{v}/applications/{a}/interviews/{i}. See
/// UpdateInterviewHandlerTests in HR.Modules.Recruitment.Tests for the unit-level equivalent.
/// Covers: anonymous 401, wrong-role 403, happy 200 + persisted reschedule, unknown application 404,
/// unknown interview 404, cross-company 404, non-pending outcome 400, duration validation 422.
/// </summary>
[Collection("Integration")]
public class UpdateInterviewEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000c5-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc0000c5-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public UpdateInterviewEndpointTests(ApiWebApplicationFactory factory)
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

    private static string Url(Guid companyId, Guid vacancyId, Guid applicationId, Guid interviewId) =>
        $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/interviews/{interviewId}";

    private static object Body(Guid companyId, Guid vacancyId, Guid applicationId, Guid interviewId,
        DateTimeOffset scheduledAt, int? durationMinutes = 60) =>
        new
        {
            companyId, vacancyId, applicationId, interviewId,
            interviewerEmployeeId = Guid.NewGuid(),
            scheduledAt,
            durationMinutes,
            location = "Room 2",
        };

    [Fact]
    public async Task Put_Interview_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            Url(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            Body(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(3)));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Interview_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyId, seeded.ApplicationId, Now);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.PutAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId),
            Body(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId, Now.AddDays(3)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Interview_Reschedules_And_Persists()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyId, seeded.ApplicationId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);
        var newSchedule = Now.AddDays(5);

        var response = await client.PutAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId),
            Body(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId, newSchedule, 90));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<InterviewPayload>();
        Assert.NotNull(payload);
        Assert.Equal(90, payload!.DurationMinutes);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var saved = await db.Interviews.SingleAsync(i => i.Id == interviewId);
        Assert.Equal(90, saved.DurationMinutes);
        Assert.Equal("Room 2", saved.Location);
    }

    [Fact]
    public async Task Put_Interview_Returns_NotFound_For_Unknown_Application()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyId, seeded.ApplicationId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            Url(companyId, seeded.VacancyId, Guid.NewGuid(), interviewId),
            Body(companyId, seeded.VacancyId, Guid.NewGuid(), interviewId, Now.AddDays(3)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Interview_Returns_NotFound_For_Unknown_Interview()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId, Guid.NewGuid()),
            Body(companyId, seeded.VacancyId, seeded.ApplicationId, Guid.NewGuid(), Now.AddDays(3)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Interview_Returns_NotFound_For_Interview_In_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyA, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyA, seeded.ApplicationId, Now);
        using var client = await ClientAs(RecruiterUser, companyB);

        var response = await client.PutAsJsonAsync(
            Url(companyB, seeded.VacancyId, seeded.ApplicationId, interviewId),
            Body(companyB, seeded.VacancyId, seeded.ApplicationId, interviewId, Now.AddDays(3)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Interview_Returns_BadRequest_When_Interview_Outcome_Not_Pending()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(
            _factory, companyId, seeded.ApplicationId, Now, recordOutcome: InterviewOutcome.Passed);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId),
            Body(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId, Now.AddDays(3)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_Interview_Returns_UnprocessableEntity_For_Non_Positive_Duration()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyId, seeded.ApplicationId, Now);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId),
            Body(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId, Now.AddDays(3), durationMinutes: 0));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record InterviewPayload(
        Guid Id, Guid CompanyId, Guid ApplicationId, Guid InterviewerEmployeeId,
        DateTimeOffset ScheduledAt, int? DurationMinutes, string? Location, string Outcome);
}
