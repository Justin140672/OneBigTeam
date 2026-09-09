using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): RecruitmentStage.version coverage for
// PUT .../recruitment-stages/{id}, against the real Postgres-backed ApiWebApplicationFactory. Follows
// UpdateRecruitmentStageEndpointTests for seeding/auth helpers. The pre-edit Version is read from
// GET .../recruitment-stages, whose list items carry it.
[Collection("Integration")]
public class UpdateRecruitmentStageConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0f0c99-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public UpdateRecruitmentStageConcurrencyEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> SeedFirstStageAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var stages = RecruitmentStageSeeder.BuildDefaultStages(companyId, Now);
        db.RecruitmentStages.AddRange(stages);
        await db.SaveChangesAsync();
        return stages[0].Id;
    }

    private static object Body(Guid companyId, Guid stageId, string name, int? expectedVersion)
        => new { companyId, recruitmentStageId = stageId, name, isTerminal = false, terminalOutcome = "None", expectedVersion };

    [Fact]
    public async Task Put_RecruitmentStage_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/recruitment-stages/{Guid.NewGuid()}",
            Body(Guid.NewGuid(), Guid.NewGuid(), "Renamed", 1));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_RecruitmentStage_Returns_NotFound_For_Unknown_Stage()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(companyId);
        var stageId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/{stageId}", Body(companyId, stageId, "Renamed", 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_RecruitmentStage_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(companyId);
        var stageId = await SeedFirstStageAsync(companyId);
        var version = await GetVersionAsync(client, companyId, stageId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/{stageId}", Body(companyId, stageId, "First Screen", version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<StagePayload>())!;
        Assert.Equal(version + 1, payload.Version);
        Assert.Equal("First Screen", payload.Name);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(companyId);
        var stageId = await SeedFirstStageAsync(companyId);
        var version = await GetVersionAsync(client, companyId, stageId);

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/{stageId}", Body(companyId, stageId, "EditorA Name", version));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(version + 1, (await editorA.Content.ReadFromJsonAsync<StagePayload>())!.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/{stageId}", Body(companyId, stageId, "EditorB Name", version));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Contains("changed by someone else", await editorB.Content.ReadAsStringAsync());

        var items = await GetItemsAsync(client, companyId);
        Assert.Equal("EditorA Name", items.Single(s => s.Id == stageId).Name);
    }

    [Fact]
    public async Task Put_RecruitmentStage_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(companyId);
        var stageId = await SeedFirstStageAsync(companyId);

        var before = (await GetItemsAsync(client, companyId)).Single(s => s.Id == stageId);

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/{stageId}", Body(companyId, stageId, "NoVersion1", null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var after = (await GetItemsAsync(client, companyId)).Single(s => s.Id == stageId);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.Version, after.Version);
    }

    private async Task<int> GetVersionAsync(HttpClient client, Guid companyId, Guid stageId)
        => (await GetItemsAsync(client, companyId)).Single(s => s.Id == stageId).Version;

    private static async Task<List<StagePayload>> GetItemsAsync(HttpClient client, Guid companyId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment-stages");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StageListPayload>())!.Items;
    }

    private sealed record StageListPayload(List<StagePayload> Items);
    private sealed record StagePayload(Guid Id, string Name, int Version);
}
