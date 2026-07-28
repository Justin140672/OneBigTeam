using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class SetRecruitmentStageActiveStatusEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000a2-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc0000a2-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public SetRecruitmentStageActiveStatusEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Dictionary<string, Guid>> SeedDefaultStagesAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var stages = RecruitmentStageSeeder.BuildDefaultStages(companyId, Now);
        db.RecruitmentStages.AddRange(stages);
        await db.SaveChangesAsync();
        return stages.ToDictionary(s => s.Name, s => s.Id);
    }

    [Fact]
    public async Task Post_ActiveStatus_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/recruitment-stages/{Guid.NewGuid()}/active-status",
            new { isActive = false });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ActiveStatus_Returns_Forbidden_For_RecruitmentView_Only_User()
    {
        var companyId = Guid.NewGuid();
        var stages = await SeedDefaultStagesAsync(companyId);
        using var client = ClientAs(PlainEmployeeUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/{stages["Offer"]}/active-status",
            new { companyId, recruitmentStageId = stages["Offer"], isActive = false });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_ActiveStatus_Deactivates_A_Non_Terminal_Stage()
    {
        var companyId = Guid.NewGuid();
        var stages = await SeedDefaultStagesAsync(companyId);
        using var client = ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/{stages["Offer"]}/active-status",
            new { companyId, recruitmentStageId = stages["Offer"], isActive = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.IsActive);
    }

    [Fact]
    public async Task Post_ActiveStatus_Returns_NotFound_For_Unknown_Stage()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/{Guid.NewGuid()}/active-status",
            new { companyId, recruitmentStageId = Guid.NewGuid(), isActive = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ActiveStatus_Returns_UnprocessableEntity_When_Deactivating_The_Only_Active_Hired_Stage()
    {
        var companyId = Guid.NewGuid();
        var stages = await SeedDefaultStagesAsync(companyId);
        using var client = ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/{stages["Hired"]}/active-status",
            new { companyId, recruitmentStageId = stages["Hired"], isActive = false });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_ActiveStatus_Returns_UnprocessableEntity_When_Deactivating_The_Last_Active_Stage()
    {
        var companyId = Guid.NewGuid();
        var stages = await SeedDefaultStagesAsync(companyId);

        // Deactivate every stage except one non-terminal stage (using direct EF access to save
        // HTTP round trips — the business rule under test is enforced on the last remaining one).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var toDeactivate = await db.RecruitmentStages
                .Where(s => s.CompanyId == companyId && s.Name != "Application Received")
                .ToListAsync();
            foreach (var stage in toDeactivate)
                stage.SetActiveStatus(false, Now);
            await db.SaveChangesAsync();
        }

        using var client = ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages/{stages["Application Received"]}/active-status",
            new { companyId, recruitmentStageId = stages["Application Received"], isActive = false });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record StatusPayload(Guid Id, Guid CompanyId, string Name, bool IsActive, DateTimeOffset UpdatedAt);
}
