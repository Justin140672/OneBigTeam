using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): Interview.version coverage for
// PUT .../vacancies/{v}/applications/{a}/interviews/{i}, against the real Postgres-backed
// ApiWebApplicationFactory. Follows UpdateInterviewEndpointTests for seeding/auth helpers. The
// pre-edit Version is read from GET .../vacancies/{v}/interviews, whose list items carry it.
[Collection("Integration")]
public class UpdateInterviewConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0f0c05-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public UpdateInterviewConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientAs(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, RecruiterUser, companyId);
        return client;
    }

    private static string Url(Guid companyId, Guid vacancyId, Guid applicationId, Guid interviewId) =>
        $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/interviews/{interviewId}";

    private static object Body(Guid companyId, Guid vacancyId, Guid applicationId, Guid interviewId, string location, int? expectedVersion)
        => new
        {
            companyId, vacancyId, applicationId, interviewId,
            interviewerEmployeeId = Guid.NewGuid(),
            scheduledAt = Now.AddDays(5),
            durationMinutes = 60,
            location,
            expectedVersion,
        };

    [Fact]
    public async Task Put_Interview_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            Url(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            Body(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Room 2", 1));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Interview_Returns_NotFound_For_Unknown_Interview()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        using var client = await ClientAs(companyId);

        var response = await client.PutAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId, Guid.NewGuid()),
            Body(companyId, seeded.VacancyId, seeded.ApplicationId, Guid.NewGuid(), "Room 2", 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Interview_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyId, seeded.ApplicationId, Now);
        using var client = await ClientAs(companyId);
        var version = await GetVersionAsync(client, companyId, seeded.VacancyId, interviewId);

        var response = await client.PutAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId),
            Body(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId, "Room 5", version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<InterviewPayload>())!;
        Assert.Equal(version + 1, payload.Version);
        Assert.Equal("Room 5", payload.Location);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyId, seeded.ApplicationId, Now);
        using var client = await ClientAs(companyId);
        var version = await GetVersionAsync(client, companyId, seeded.VacancyId, interviewId);

        var editorA = await client.PutAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId),
            Body(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId, "Room A", version));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(version + 1, (await editorA.Content.ReadFromJsonAsync<InterviewPayload>())!.Version);

        var editorB = await client.PutAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId),
            Body(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId, "Room B", version));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Contains("changed by someone else", await editorB.Content.ReadAsStringAsync());

        var items = await GetItemsAsync(client, companyId, seeded.VacancyId);
        Assert.Equal("Room A", items.Single(i => i.Id == interviewId).Location);
    }

    [Fact]
    public async Task Put_Interview_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyId, seeded.ApplicationId, Now);
        using var client = await ClientAs(companyId);

        var before = (await GetItemsAsync(client, companyId, seeded.VacancyId)).Single(i => i.Id == interviewId).Location;

        var r1 = await client.PutAsJsonAsync(
            Url(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId),
            Body(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId, "Room 1", null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var after = (await GetItemsAsync(client, companyId, seeded.VacancyId)).Single(i => i.Id == interviewId).Location;
        Assert.Equal(before, after);
    }

    private async Task<int> GetVersionAsync(HttpClient client, Guid companyId, Guid vacancyId, Guid interviewId)
        => (await GetItemsAsync(client, companyId, vacancyId)).Single(i => i.Id == interviewId).Version;

    private static async Task<List<InterviewPayload>> GetItemsAsync(HttpClient client, Guid companyId, Guid vacancyId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}/interviews");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InterviewListPayload>())!.Items;
    }

    private sealed record InterviewListPayload(List<InterviewPayload> Items);
    private sealed record InterviewPayload(Guid Id, string? Location, int? DurationMinutes, int Version);
}
