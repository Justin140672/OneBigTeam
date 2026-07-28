using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class ReorderRecruitmentStagesEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000a1-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc0000a1-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public ReorderRecruitmentStagesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployeeUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private HttpClient ClientAs(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<Guid[]> SeedDefaultStagesAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var stages = RecruitmentStageSeeder.BuildDefaultStages(companyId, Now);
        db.RecruitmentStages.AddRange(stages);
        await db.SaveChangesAsync();
        return stages.Select(s => s.Id).ToArray();
    }

    [Fact]
    public async Task Post_Reorder_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/recruitment-stages/reorder",
            new { orderedStageIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Reorder_Returns_Forbidden_For_RecruitmentView_Only_User()
    {
        var companyId = Guid.NewGuid();
        var stageIds = await SeedDefaultStagesAsync(companyId);
        using var client = ClientAs(PlainEmployeeUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/reorder",
            new { companyId, orderedStageIds = stageIds });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Reorder_Reassigns_DisplayOrder_By_List_Position()
    {
        var companyId = Guid.NewGuid();
        var stageIds = await SeedDefaultStagesAsync(companyId);
        using var client = ClientAs(RecruiterUser, companyId);

        var reversed = stageIds.Reverse().ToArray();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/reorder",
            new { companyId, orderedStageIds = reversed });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReorderPayload>();
        Assert.NotNull(payload);
        Assert.Equal(reversed, payload!.Items.Select(i => i.Id).ToArray());
        Assert.Equal([1, 2, 3, 4, 5, 6], payload.Items.Select(i => i.DisplayOrder));

        using var listClient = ClientAs(RecruiterUser, companyId);
        var listResponse = await listClient.GetAsync($"/api/companies/{companyId}/recruitment-stages");
        var listPayload = await listResponse.Content.ReadFromJsonAsync<ListPayload>();
        Assert.Equal(reversed, listPayload!.Items.Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task Post_Reorder_Returns_UnprocessableEntity_When_A_Stage_Is_Omitted()
    {
        var companyId = Guid.NewGuid();
        var stageIds = await SeedDefaultStagesAsync(companyId);
        using var client = ClientAs(RecruiterUser, companyId);

        var partial = stageIds.Take(stageIds.Length - 1).ToArray();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/reorder",
            new { companyId, orderedStageIds = partial });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Reorder_Returns_UnprocessableEntity_When_An_Unknown_Stage_Id_Is_Included()
    {
        var companyId = Guid.NewGuid();
        var stageIds = await SeedDefaultStagesAsync(companyId);
        using var client = ClientAs(RecruiterUser, companyId);

        var withUnknown = stageIds.Skip(1).Append(Guid.NewGuid()).ToArray();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/reorder",
            new { companyId, orderedStageIds = withUnknown });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Reorder_Returns_UnprocessableEntity_For_Empty_List()
    {
        var companyId = Guid.NewGuid();
        await SeedDefaultStagesAsync(companyId);
        using var client = ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/reorder",
            new { companyId, orderedStageIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record ReorderPayload(List<ReorderedItem> Items);
    private sealed record ReorderedItem(Guid Id, string Name, int DisplayOrder);
    private sealed record ListPayload(List<StageItem> Items);
    private sealed record StageItem(Guid Id, string Name, int DisplayOrder);
}
