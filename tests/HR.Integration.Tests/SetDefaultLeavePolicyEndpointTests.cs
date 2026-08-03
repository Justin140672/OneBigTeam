using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class SetDefaultLeavePolicyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUser    = Guid.Parse("11100009-0000-0000-0000-000000000001");
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public SetDefaultLeavePolicyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task SetDefault_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies/{Guid.NewGuid()}/set-default", EmptyJson());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SetDefault_Returns_NotFound_For_Unknown_Policy()
    {
        using var client = HrAdminClient();
        var response     = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/leave-policies/{Guid.NewGuid()}/set-default", EmptyJson());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetDefault_Returns_BadRequest_For_Inactive_Policy()
    {
        var newCompanyId = Guid.NewGuid();
        using var client = HrAdminClient(newCompanyId);
        var now = DateTimeOffset.UtcNow;

        // Seed an inactive, non-default policy directly via the DbContext — no deactivate
        // endpoint exists yet for LeavePolicy.
        Guid inactivePolicyId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();

            var defaultPolicy = LeavePolicy.Create(Guid.NewGuid(), newCompanyId, "Default Policy", null, 0, false, true, now);
            var inactivePolicy = LeavePolicy.Create(Guid.NewGuid(), newCompanyId, "Inactive Policy", null, 0, false, false, now);
            inactivePolicy.Deactivate(now);
            inactivePolicyId = inactivePolicy.Id;

            db.LeavePolicies.AddRange(defaultPolicy, inactivePolicy);
            await db.SaveChangesAsync();
        }

        var setDefaultResp = await client.PostAsync(
            $"/api/companies/{newCompanyId}/leave-policies/{inactivePolicyId}/set-default", EmptyJson());
        Assert.Equal(HttpStatusCode.BadRequest, setDefaultResp.StatusCode);
    }

    [Fact]
    public async Task SetDefault_Returns_NoContent_And_Swaps_Default_On_Happy_Path()
    {
        var newCompanyId = Guid.NewGuid();
        using var client = HrAdminClient(newCompanyId);

        var firstResp = await client.PostAsJsonAsync(
            $"/api/companies/{newCompanyId}/leave-policies",
            new { companyId = newCompanyId, name = $"First {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false, isDefault = false });
        firstResp.EnsureSuccessStatusCode();
        var first = await firstResp.Content.ReadFromJsonAsync<PolicyPayload>();
        Assert.True(first!.IsDefault);

        var secondResp = await client.PostAsJsonAsync(
            $"/api/companies/{newCompanyId}/leave-policies",
            new { companyId = newCompanyId, name = $"Second {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false, isDefault = false });
        secondResp.EnsureSuccessStatusCode();
        var second = await secondResp.Content.ReadFromJsonAsync<PolicyPayload>();
        Assert.False(second!.IsDefault);

        var setDefaultResp = await client.PostAsync(
            $"/api/companies/{newCompanyId}/leave-policies/{second.Id}/set-default", EmptyJson());
        Assert.Equal(HttpStatusCode.NoContent, setDefaultResp.StatusCode);

        var reloadedFirst = await (await client.GetAsync(
            $"/api/companies/{newCompanyId}/leave-policies/{first.Id}")).Content.ReadFromJsonAsync<PolicyPayload>();
        var reloadedSecond = await (await client.GetAsync(
            $"/api/companies/{newCompanyId}/leave-policies/{second.Id}")).Content.ReadFromJsonAsync<PolicyPayload>();

        Assert.False(reloadedFirst!.IsDefault);
        Assert.True(reloadedSecond!.IsDefault);
    }

    private HttpClient HrAdminClient(Guid? companyId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, (companyId ?? SeededCompanyId).ToString());
        return client;
    }

    private static StringContent EmptyJson() => new("{}", Encoding.UTF8, "application/json");

    private sealed record PolicyPayload(Guid Id, string Name, int CarryOverDays, bool AllowNegativeBalance, bool IsDefault);
}
