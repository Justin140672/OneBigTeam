using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): PositionProfile.version coverage for
// PUT .../position-profiles/{id}, against the real Postgres-backed ApiWebApplicationFactory.
// Follows UpdatePositionProfileEndpointTests for seeding/auth helpers.
[Collection("Integration")]
public class UpdatePositionProfileConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eecc0000-0000-0000-0000-000000000051");

    public UpdatePositionProfileConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
        // GetPositionProfile (used to read the pre-edit Version) is gated on role:employee.
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_PositionProfile_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/position-profiles/{Guid.NewGuid()}", new { title = "X" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var (client, companyId, ctx) = await SeedAsync();
        var loaded = await GetAsync(client, companyId, ctx.ProfileId);
        var version = loaded.Version;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{ctx.ProfileId}",
            Body(companyId, ctx, "Editor A Title", version));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(version + 1, (await editorA.Content.ReadFromJsonAsync<ProfilePayload>())!.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{ctx.ProfileId}",
            Body(companyId, ctx, "Editor B Title", version));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Equal("concurrency", (await editorB.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        var after = await GetAsync(client, companyId, ctx.ProfileId);
        Assert.Equal("Editor A Title", after.Title);
    }

    [Fact]
    public async Task Put_PositionProfile_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, ctx) = await SeedAsync();
        var loaded = await GetAsync(client, companyId, ctx.ProfileId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{ctx.ProfileId}",
            Body(companyId, ctx, "Updated", loaded.Version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<ProfilePayload>())!;
        Assert.Equal(loaded.Version + 1, payload.Version);
        Assert.Equal("Updated", payload.Title);
    }

    [Fact]
    public async Task Put_PositionProfile_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var (client, companyId, ctx) = await SeedAsync();

        var before = await GetAsync(client, companyId, ctx.ProfileId);

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{ctx.ProfileId}", Body(companyId, ctx, "NoVersion1", null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var after = await GetAsync(client, companyId, ctx.ProfileId);
        Assert.Equal(before.Title, after.Title);
    }

    private static object Body(Guid companyId, SeedContext ctx, string title, int? expectedVersion)
        => new
        {
            companyId,
            id = ctx.ProfileId,
            departmentId = ctx.DepartmentId,
            locationId = ctx.LocationId,
            defaultLeavePolicyId = ctx.LeavePolicyId,
            title,
            expectedVersion
        };

    private async Task<(HttpClient Client, Guid CompanyId, SeedContext Ctx)> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.HrAdministrator, companyId);

        var departmentId = await PostIdAsync(client, $"/api/companies/{companyId}/departments",
            new { companyId, name = $"Engineering {Guid.NewGuid():N}" });

        var locationTypeId = await PostIdAsync(client, $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"Office {Guid.NewGuid():N}" });
        var locationId = await PostIdAsync(client, $"/api/companies/{companyId}/locations",
            new { companyId, name = $"HQ {Guid.NewGuid():N}", locationTypeId });

        var leavePolicyId = await PostIdAsync(client, $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Standard {Guid.NewGuid():N}", carryOverDays = 5, allowNegativeBalance = false });

        var profileId = await PostIdAsync(client, $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, defaultLeavePolicyId = leavePolicyId, title = $"Original {Guid.NewGuid():N}" });

        return (client, companyId, new SeedContext(departmentId, locationId, leavePolicyId, profileId));
    }

    private static async Task<Guid> PostIdAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private static async Task<ProfilePayload> GetAsync(HttpClient client, Guid companyId, Guid profileId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/position-profiles/{profileId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProfilePayload>())!;
    }

    private sealed record SeedContext(Guid DepartmentId, Guid LocationId, Guid LeavePolicyId, Guid ProfileId);
    private sealed record IdPayload(Guid Id);
    private sealed record ErrorPayload(string? Error, string? Code);
    private sealed record ProfilePayload(Guid Id, string Title, int Version);
}
