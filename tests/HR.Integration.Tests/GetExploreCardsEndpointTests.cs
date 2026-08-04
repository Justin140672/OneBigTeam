using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetExploreCardsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUserId = new("cc000003-0000-0000-0000-000000000001");

    public GetExploreCardsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    [Fact]
    public async Task Get_ExploreCards_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/company-onboarding/explore-cards");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ExploreCards_Returns_Six_Static_Cards_With_Reports_ComingSoon()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, HrAdminUserId);

        var response = await client.GetAsync("/api/company-onboarding/explore-cards");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ExploreCardsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(6, payload!.Cards.Count);

        var reports = Assert.Single(payload.Cards, c => c.Name == "Reports");
        Assert.True(reports.IsComingSoon);

        Assert.All(payload.Cards.Where(c => c.Name != "Reports"), c => Assert.False(c.IsComingSoon));
    }

    private sealed record ExploreCardPayload(string Name, string Description, string LinkUrl, bool IsComingSoon);

    private sealed record ExploreCardsPayload(List<ExploreCardPayload> Cards);
}
