using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListRecruitmentStagesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc000097-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc000097-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public ListRecruitmentStagesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployeeUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientAs(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private async Task SeedDefaultStagesAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        db.RecruitmentStages.AddRange(RecruitmentStageSeeder.BuildDefaultStages(companyId, Now));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_RecruitmentStages_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/recruitment-stages");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_RecruitmentStages_Returns_Ok_For_RecruitmentView_User()
    {
        var companyId = Guid.NewGuid();
        await SeedDefaultStagesAsync(companyId);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment-stages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(6, payload!.Items.Count);
    }

    [Fact]
    public async Task Get_RecruitmentStages_Returns_Stages_Ordered_By_DisplayOrder_Including_Inactive()
    {
        var companyId = Guid.NewGuid();
        await SeedDefaultStagesAsync(companyId);

        using var client = await ClientAs(RecruiterUser, companyId);
        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment-stages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(
            ["Application Received", "CV Review", "Interview", "Offer", "Hired", "Rejected"],
            payload!.Items.Select(i => i.Name).ToArray());
        Assert.Equal([1, 2, 3, 4, 5, 6], payload.Items.Select(i => i.DisplayOrder));
    }

    [Fact]
    public async Task Get_RecruitmentStages_Isolates_By_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        await SeedDefaultStagesAsync(otherCompanyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment-stages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record ListPayload(List<StageItem> Items);
    private sealed record StageItem(Guid Id, string Name, int DisplayOrder, bool IsActive, bool IsTerminal, string TerminalOutcome);
}
