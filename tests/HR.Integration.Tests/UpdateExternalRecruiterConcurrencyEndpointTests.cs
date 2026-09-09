using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): ExternalRecruiter.version coverage for
// PUT .../external-recruiters/{id}, against the real Postgres-backed ApiWebApplicationFactory.
// Follows UpdateExternalRecruiterEndpointTests for seeding/auth helpers. The pre-edit Version is read
// from GET .../external-recruiters/{id}, which carries it (the list item does not).
[Collection("Integration")]
public class UpdateExternalRecruiterConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cd0f000c-0000-0000-0000-000000000001");

    public UpdateExternalRecruiterConcurrencyEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> SeedRecruiterAsync(HttpClient client, Guid companyId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/external-recruiters", new { companyId, agencyName = "Acme Recruiting" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecruiterPayload>())!.Id;
    }

    private static object Body(Guid companyId, Guid recruiterId, string agencyName, int? expectedVersion)
        => new { companyId, externalRecruiterId = recruiterId, agencyName, expectedVersion };

    [Fact]
    public async Task Put_ExternalRecruiter_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/external-recruiters/{Guid.NewGuid()}",
            Body(Guid.NewGuid(), Guid.NewGuid(), "Updated Name", 1));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_ExternalRecruiter_Returns_NotFound_For_Unknown_Recruiter()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(companyId);
        var recruiterId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/external-recruiters/{recruiterId}", Body(companyId, recruiterId, "Updated Name", 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_ExternalRecruiter_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(companyId);
        var recruiterId = await SeedRecruiterAsync(client, companyId);
        var version = (await GetAsync(client, companyId, recruiterId)).Version;

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/external-recruiters/{recruiterId}", Body(companyId, recruiterId, "Updated Name", version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<RecruiterPayload>())!;
        Assert.Equal(version + 1, payload.Version);
        Assert.Equal("Updated Name", payload.AgencyName);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(companyId);
        var recruiterId = await SeedRecruiterAsync(client, companyId);
        var version = (await GetAsync(client, companyId, recruiterId)).Version;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/external-recruiters/{recruiterId}", Body(companyId, recruiterId, "EditorA Agency", version));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(version + 1, (await editorA.Content.ReadFromJsonAsync<RecruiterPayload>())!.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/external-recruiters/{recruiterId}", Body(companyId, recruiterId, "EditorB Agency", version));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Contains("changed by someone else", await editorB.Content.ReadAsStringAsync());

        Assert.Equal("EditorA Agency", (await GetAsync(client, companyId, recruiterId)).AgencyName);
    }

    [Fact]
    public async Task Put_ExternalRecruiter_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(companyId);
        var recruiterId = await SeedRecruiterAsync(client, companyId);

        var before = await GetAsync(client, companyId, recruiterId);

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/external-recruiters/{recruiterId}", Body(companyId, recruiterId, "NoVersion1", null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var after = await GetAsync(client, companyId, recruiterId);
        Assert.Equal(before.AgencyName, after.AgencyName);
    }

    private static async Task<RecruiterPayload> GetAsync(HttpClient client, Guid companyId, Guid recruiterId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/external-recruiters/{recruiterId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecruiterPayload>())!;
    }

    private sealed record RecruiterPayload(Guid Id, string AgencyName, int Version);
}
