using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (optimistic concurrency rollout): LeavePolicy.version coverage for
// PUT .../leave-policies/{id}, against the real Postgres-backed ApiWebApplicationFactory. Follows
// LeavePolicyCrudEndpointTests for auth helpers.
[Collection("Integration")]
public class UpdateLeavePolicyConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUser = Guid.Parse("11cc0008-0000-0000-0000-000000000001");

    public UpdateLeavePolicyConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_Policy_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/leave-policies/{Guid.NewGuid()}", new { name = "X" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Two_Editors_Second_Stale_Save_Returns_409_Concurrency_And_First_Values_Preserved()
    {
        var (client, companyId, policyId) = await CreatePolicyAsync();
        var loaded = await GetAsync(client, companyId, policyId);
        var version = loaded.Version;

        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies/{policyId}",
            Body(companyId, policyId, loaded.Name, carryOver: 10, expectedVersion: version));
        Assert.Equal(HttpStatusCode.OK, editorA.StatusCode);
        Assert.Equal(version + 1, (await editorA.Content.ReadFromJsonAsync<PolicyPayload>())!.Version);

        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies/{policyId}",
            Body(companyId, policyId, loaded.Name, carryOver: 20, expectedVersion: version));

        Assert.Equal(HttpStatusCode.Conflict, editorB.StatusCode);
        Assert.Equal("concurrency", (await editorB.Content.ReadFromJsonAsync<ErrorPayload>())!.Code);

        var after = await GetAsync(client, companyId, policyId);
        Assert.Equal(10, after.CarryOverDays);
    }

    [Fact]
    public async Task Put_Policy_With_Correct_ExpectedVersion_Succeeds_And_Increments_Version()
    {
        var (client, companyId, policyId) = await CreatePolicyAsync();
        var loaded = await GetAsync(client, companyId, policyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies/{policyId}",
            Body(companyId, policyId, loaded.Name, carryOver: 7, expectedVersion: loaded.Version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<PolicyPayload>())!;
        Assert.Equal(loaded.Version + 1, payload.Version);
        Assert.Equal(7, payload.CarryOverDays);
    }

    [Fact]
    public async Task Put_Policy_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var (client, companyId, policyId) = await CreatePolicyAsync();
        var loaded = await GetAsync(client, companyId, policyId);

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies/{policyId}",
            Body(companyId, policyId, loaded.Name, carryOver: 3, expectedVersion: null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var after = await GetAsync(client, companyId, policyId);
        Assert.Equal(loaded.CarryOverDays, after.CarryOverDays);
        Assert.Equal(loaded.Version, after.Version);
    }

    private static object Body(Guid companyId, Guid policyId, string name, int carryOver, int? expectedVersion)
        => new
        {
            companyId,
            policyId,
            name,
            carryOverDays = carryOver,
            allowNegativeBalance = false,
            // The first policy created for a fresh company is that company's default; UpdateLeavePolicy
            // rejects a request that would unset the only default, so echo it back as true.
            isDefault = true,
            requiresApproval = true,
            expectedVersion
        };

    private async Task<(HttpClient Client, Guid CompanyId, Guid PolicyId)> CreatePolicyAsync()
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Policy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<PolicyPayload>())!;
        return (client, companyId, created.Id);
    }

    private static async Task<PolicyPayload> GetAsync(HttpClient client, Guid companyId, Guid policyId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/leave-policies/{policyId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PolicyPayload>())!;
    }

    private sealed record ErrorPayload(string? Error, string? Code);
    private sealed record PolicyPayload(Guid Id, string Name, int CarryOverDays, bool AllowNegativeBalance, bool IsDefault, int Version);
}
