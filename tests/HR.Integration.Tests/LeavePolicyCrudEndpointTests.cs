using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class LeavePolicyCrudEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    // leave:approve = Manager, HrAdministrator
    // leave:manage  = HrAdministrator only (CompanyAdministrator is scoped to
    //                  company profile/settings and does not hold it)
    private static readonly Guid HrAdminUser     = Guid.Parse("11100008-0000-0000-0000-000000000001");
    private static readonly Guid ManagerUser      = Guid.Parse("11100008-0000-0000-0000-000000000002");
    private static readonly Guid SeededCompanyId  = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public LeavePolicyCrudEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser,  SystemRoles.Manager);
        }).GetAwaiter().GetResult();
    }

    // ── ListLeavePolicies ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListPolicies_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.GetAsync($"/api/companies/{SeededCompanyId}/leave-policies");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListPolicies_Returns_Forbidden_Without_Leave_Approve_Role()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());

        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/leave-policies");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListPolicies_Returns_OK_For_Manager_Role()
    {
        // Manager has leave:approve — should be able to list
        using var client = ManagerClient();
        var response     = await client.GetAsync($"/api/companies/{SeededCompanyId}/leave-policies");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListPolicies_Returns_Created_Policy()
    {
        using var client = HrAdminClient();
        var policyName   = $"List Test Policy {Guid.NewGuid():N}";

        await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies",
            new { companyId = SeededCompanyId, name = policyName, carryOverDays = 5, allowNegativeBalance = false });

        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/leave-policies");
        var payload  = await response.Content.ReadFromJsonAsync<ListPoliciesPayload>();
        Assert.Contains(payload!.Items, p => p.Name == policyName);
    }

    // ── GetLeavePolicy ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPolicy_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPolicy_Returns_Forbidden_Without_Leave_Approve_Role()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPolicy_Returns_NotFound_For_Unknown_Id()
    {
        using var client = HrAdminClient();
        var response     = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPolicy_Returns_Policy_By_Id()
    {
        using var client = HrAdminClient();
        var policyName   = $"Get Test Policy {Guid.NewGuid():N}";

        var createResp = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies",
            new { companyId = SeededCompanyId, name = policyName, carryOverDays = 3, allowNegativeBalance = true });
        createResp.EnsureSuccessStatusCode();
        var created  = await createResp.Content.ReadFromJsonAsync<PolicyPayload>();

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PolicyPayload>();
        Assert.Equal(created.Id,  payload!.Id);
        Assert.Equal(policyName,  payload.Name);
        Assert.Equal(3,           payload.CarryOverDays);
        Assert.True(              payload.AllowNegativeBalance);
    }

    // ── UpdateLeavePolicy ────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdatePolicy_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies/{Guid.NewGuid()}",
            new { name = "Updated" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePolicy_Returns_Forbidden_Without_Leave_Manage_Role()
    {
        // Manager has leave:approve but NOT leave:manage
        using var client = ManagerClient();
        var response     = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies/{Guid.NewGuid()}",
            new { name = "Updated" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePolicy_Returns_NotFound_For_Unknown_Id()
    {
        using var client  = HrAdminClient();
        var unknownId     = Guid.NewGuid();
        var response      = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies/{unknownId}",
            new { companyId = SeededCompanyId, policyId = unknownId, name = "Ghost", carryOverDays = 0, allowNegativeBalance = false });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePolicy_Returns_OK_And_Persists_Changes()
    {
        using var client = HrAdminClient();

        var createResp = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies",
            new { companyId = SeededCompanyId, name = $"Before Update {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<PolicyPayload>();

        var updatedName  = $"After Update {Guid.NewGuid():N}";
        var updateResp   = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies/{created!.Id}",
            new
            {
                companyId          = SeededCompanyId,
                policyId           = created.Id,
                name               = updatedName,
                description        = "Updated description",
                carryOverDays      = 10,
                allowNegativeBalance = true
            });

        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updated = await updateResp.Content.ReadFromJsonAsync<PolicyPayload>();
        Assert.Equal(updatedName,  updated!.Name);
        Assert.Equal(10,           updated.CarryOverDays);
        Assert.True(               updated.AllowNegativeBalance);
    }

    [Fact]
    public async Task UpdatePolicy_Returns_Conflict_When_Name_Already_Taken()
    {
        using var client = HrAdminClient();
        var existingName = $"Conflict Policy {Guid.NewGuid():N}";

        // Create the "existing" policy
        await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies",
            new { companyId = SeededCompanyId, name = existingName, carryOverDays = 0, allowNegativeBalance = false });

        // Create a second policy to rename
        var secondResp = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies",
            new { companyId = SeededCompanyId, name = $"Second Policy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        var second = await secondResp.Content.ReadFromJsonAsync<PolicyPayload>();

        // Try to rename it to the existing name
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies/{second!.Id}",
            new { companyId = SeededCompanyId, policyId = second.Id, name = existingName, carryOverDays = 0, allowNegativeBalance = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── IsDefault behavior ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePolicy_First_Policy_For_New_Company_Is_Forced_Default()
    {
        var newCompanyId = Guid.NewGuid();
        using var client = HrAdminClient(newCompanyId);
        var policyName   = $"First Policy {Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{newCompanyId}/leave-policies",
            new { companyId = newCompanyId, name = policyName, carryOverDays = 0, allowNegativeBalance = false, isDefault = false });

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<PolicyPayload>();
        Assert.True(created!.IsDefault);
    }

    [Fact]
    public async Task CreatePolicy_With_IsDefault_True_Unmarks_Existing_Default()
    {
        var newCompanyId = Guid.NewGuid();
        using var client = HrAdminClient(newCompanyId);

        var firstResp = await client.PostAsJsonAsync(
            $"/api/companies/{newCompanyId}/leave-policies",
            new { companyId = newCompanyId, name = $"First {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false, isDefault = false });
        firstResp.EnsureSuccessStatusCode();
        var first = await firstResp.Content.ReadFromJsonAsync<PolicyPayload>();

        var secondResp = await client.PostAsJsonAsync(
            $"/api/companies/{newCompanyId}/leave-policies",
            new { companyId = newCompanyId, name = $"Second {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false, isDefault = true });
        secondResp.EnsureSuccessStatusCode();
        var second = await secondResp.Content.ReadFromJsonAsync<PolicyPayload>();

        Assert.True(second!.IsDefault);

        var reloadedFirst = await (await client.GetAsync(
            $"/api/companies/{newCompanyId}/leave-policies/{first!.Id}")).Content.ReadFromJsonAsync<PolicyPayload>();
        Assert.False(reloadedFirst!.IsDefault);
    }

    [Fact]
    public async Task UpdatePolicy_Returns_BadRequest_When_Removing_Default_From_Only_Default_Policy()
    {
        var newCompanyId = Guid.NewGuid();
        using var client = HrAdminClient(newCompanyId);

        var createResp = await client.PostAsJsonAsync(
            $"/api/companies/{newCompanyId}/leave-policies",
            new { companyId = newCompanyId, name = $"Only Policy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false, isDefault = false });
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<PolicyPayload>();
        Assert.True(created!.IsDefault);

        var updateResp = await client.PutAsJsonAsync(
            $"/api/companies/{newCompanyId}/leave-policies/{created.Id}",
            new
            {
                companyId     = newCompanyId,
                policyId      = created.Id,
                name          = created.Name,
                carryOverDays = 0,
                allowNegativeBalance = false,
                isDefault     = false
            });

        Assert.Equal(HttpStatusCode.BadRequest, updateResp.StatusCode);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private HttpClient HrAdminClient(Guid? companyId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, (companyId ?? SeededCompanyId).ToString());
        return client;
    }

    private HttpClient ManagerClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ManagerUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        return client;
    }

    private sealed record PolicyPayload(Guid Id, string Name, int CarryOverDays, bool AllowNegativeBalance, bool IsDefault);
    private sealed record ListPoliciesPayload(IReadOnlyList<PolicyItem> Items);
    private sealed record PolicyItem(Guid Id, string Name);
}
