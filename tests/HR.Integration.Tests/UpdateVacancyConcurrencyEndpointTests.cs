using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): Vacancy.version coverage for PUT .../vacancies/{id},
// against the real Postgres-backed ApiWebApplicationFactory. Follows UpdateVacancyEndpointTests for
// seeding/auth helpers. The pre-edit Version is read from GET .../vacancies/{id}, which carries it.
[Collection("Integration")]
public class UpdateVacancyConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0f0c14-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public UpdateVacancyConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientAs(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, RecruiterUser, SystemRoles.Recruiter, companyId);
        return client;
    }

    private static object Body(Guid companyId, Guid vacancyId, Guid hiringManagerId, string title, int? expectedVersion)
        => new { companyId, vacancyId, advertTitle = title, hiringManagerId, expectedVersion };

    [Fact]
    public async Task Put_Vacancy_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}",
            Body(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "T", 1));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Vacancy_Returns_NotFound_For_Unknown_Vacancy()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(companyId);
        var vacancyId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}",
            Body(companyId, vacancyId, Guid.NewGuid(), "T", 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Vacancy_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(companyId);
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var version = (await GetAsync(client, companyId, vacancyId)).Version;

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}", Body(companyId, vacancyId, Guid.NewGuid(), "Updated Title", version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<VacancyPayload>())!;
        Assert.Equal(version + 1, payload.Version);
        Assert.Equal("Updated Title", payload.AdvertTitle);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(companyId);
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);
        var version = (await GetAsync(client, companyId, vacancyId)).Version;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}", Body(companyId, vacancyId, Guid.NewGuid(), "EditorA Title", version));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(version + 1, (await editorA.Content.ReadFromJsonAsync<VacancyPayload>())!.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}", Body(companyId, vacancyId, Guid.NewGuid(), "EditorB Title", version));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Contains("changed by someone else", await editorB.Content.ReadAsStringAsync());

        Assert.Equal("EditorA Title", (await GetAsync(client, companyId, vacancyId)).AdvertTitle);
    }

    [Fact]
    public async Task Put_Vacancy_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(companyId);
        var vacancyId = await RecruitmentTestSeeder.SeedVacancyAsync(_factory, companyId, Now);

        var before = await GetAsync(client, companyId, vacancyId);

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}", Body(companyId, vacancyId, Guid.NewGuid(), "NoVersion1", null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var after = await GetAsync(client, companyId, vacancyId);
        Assert.Equal(before.AdvertTitle, after.AdvertTitle);
        Assert.Equal(before.Version, after.Version);
    }

    private static async Task<VacancyPayload> GetAsync(HttpClient client, Guid companyId, Guid vacancyId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VacancyPayload>())!;
    }

    private sealed record VacancyPayload(Guid Id, string? AdvertTitle, int Version);
}
