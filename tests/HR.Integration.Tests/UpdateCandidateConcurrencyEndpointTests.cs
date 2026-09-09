using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): Candidate.version coverage for PUT .../candidates/{id},
// against the real Postgres-backed ApiWebApplicationFactory. Follows UpdateCandidateEndpointTests for
// seeding/auth helpers. The pre-edit Version is read from GET .../candidates/{id}, which carries it.
[Collection("Integration")]
public class UpdateCandidateConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0f0c02-0000-0000-0000-000000000001");

    public UpdateCandidateConcurrencyEndpointTests(ApiWebApplicationFactory factory)
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
        await TestRoleSeeder.AssignRoleAsync(_factory, RecruiterUser, SystemRoles.Recruiter, companyId);
        return client;
    }

    private static object Body(Guid companyId, Guid candidateId, string email, string last, int? expectedVersion)
        => new { companyId, candidateId, firstName = "Emma", lastName = last, email, phone = "+44 7700 900123", expectedVersion };

    [Fact]
    public async Task Put_Candidate_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/candidates/{Guid.NewGuid()}",
            Body(Guid.NewGuid(), Guid.NewGuid(), "x@example.com", "X", 1));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Candidate_Returns_NotFound_For_Unknown_Candidate()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(companyId);
        var candidateId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}",
            Body(companyId, candidateId, "unknown@example.com", "Clarke", 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Candidate_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(companyId);
        var email = $"emma.{Guid.NewGuid():N}@example.com";
        var candidateId = await SeedCandidateAsync(client, companyId, email);
        var version = (await GetAsync(client, companyId, candidateId)).Version;

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}",
            Body(companyId, candidateId, email, "Updated", version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<CandidatePayload>())!;
        Assert.Equal(version + 1, payload.Version);
        Assert.Equal("Updated", payload.LastName);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(companyId);
        var email = $"emma.{Guid.NewGuid():N}@example.com";
        var candidateId = await SeedCandidateAsync(client, companyId, email);
        var version = (await GetAsync(client, companyId, candidateId)).Version;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}", Body(companyId, candidateId, email, "EditorA", version));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(version + 1, (await editorA.Content.ReadFromJsonAsync<CandidatePayload>())!.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}", Body(companyId, candidateId, email, "EditorB", version));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Contains("changed by someone else", await editorB.Content.ReadAsStringAsync());

        Assert.Equal("EditorA", (await GetAsync(client, companyId, candidateId)).LastName);
    }

    [Fact]
    public async Task Put_Candidate_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(companyId);
        var email = $"emma.{Guid.NewGuid():N}@example.com";
        var candidateId = await SeedCandidateAsync(client, companyId, email);

        var before = await GetAsync(client, companyId, candidateId);

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}", Body(companyId, candidateId, email, "NoVersion1", null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var after = await GetAsync(client, companyId, candidateId);
        Assert.Equal(before.LastName, after.LastName);
    }

    private async Task<Guid> SeedCandidateAsync(HttpClient client, Guid companyId, string email)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/candidates",
            new { companyId, firstName = "Emma", lastName = "Clarke", email });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CandidatePayload>())!.Id;
    }

    private static async Task<CandidatePayload> GetAsync(HttpClient client, Guid companyId, Guid candidateId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/candidates/{candidateId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CandidatePayload>())!;
    }

    private sealed record CandidatePayload(Guid Id, string FirstName, string LastName, string Email, int Version);
}
