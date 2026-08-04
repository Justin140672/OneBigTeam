using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateRecruitmentStageEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc000099-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc000099-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public UpdateRecruitmentStageEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Put_RecruitmentStage_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/recruitment-stages/{Guid.NewGuid()}",
            new { name = "Renamed", isTerminal = false, terminalOutcome = "None" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_RecruitmentStage_Returns_Forbidden_For_RecruitmentView_Only_User()
    {
        var companyId = Guid.NewGuid();
        var stageIds = await SeedDefaultStagesAsync(companyId);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/{stageIds[0]}",
            new { companyId, recruitmentStageId = stageIds[0], name = "Renamed", isTerminal = false, terminalOutcome = "None" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_RecruitmentStage_Updates_Name()
    {
        var companyId = Guid.NewGuid();
        var stageIds = await SeedDefaultStagesAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/{stageIds[0]}",
            new { companyId, recruitmentStageId = stageIds[0], name = "First Screen", isTerminal = false, terminalOutcome = "None" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StagePayload>();
        Assert.NotNull(payload);
        Assert.Equal("First Screen", payload!.Name);
    }

    [Fact]
    public async Task Put_RecruitmentStage_Returns_NotFound_For_Unknown_Stage()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/{Guid.NewGuid()}",
            new { companyId, recruitmentStageId = Guid.NewGuid(), name = "Renamed", isTerminal = false, terminalOutcome = "None" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_RecruitmentStage_Returns_UnprocessableEntity_For_Duplicate_Name()
    {
        var companyId = Guid.NewGuid();
        var stageIds = await SeedDefaultStagesAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/{stageIds[0]}",
            new { companyId, recruitmentStageId = stageIds[0], name = "CV Review", isTerminal = false, terminalOutcome = "None" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record StagePayload(
        Guid Id, Guid CompanyId, string Name, int DisplayOrder, bool IsActive, bool IsTerminal,
        string TerminalOutcome, DateTimeOffset UpdatedAt);
}
