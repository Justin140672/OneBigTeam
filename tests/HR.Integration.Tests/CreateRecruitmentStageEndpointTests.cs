using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateRecruitmentStageEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc000098-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc000098-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public CreateRecruitmentStageEndpointTests(ApiWebApplicationFactory factory)
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

    [Fact]
    public async Task Post_RecruitmentStages_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/recruitment-stages",
            new { name = "Technical Test", displayOrder = 10, isTerminal = false, terminalOutcome = "None" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_RecruitmentStages_Returns_Forbidden_For_RecruitmentView_Only_User()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages",
            new { companyId, name = "Technical Test", displayOrder = 10, isTerminal = false, terminalOutcome = "None" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_RecruitmentStages_Creates_Stage()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages",
            new { companyId, name = "Technical Test", displayOrder = 10, isTerminal = false, terminalOutcome = "None" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<StagePayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Technical Test", payload.Name);
        Assert.Equal(10, payload.DisplayOrder);
        Assert.True(payload.IsActive);
        Assert.False(payload.IsTerminal);
    }

    [Fact]
    public async Task Post_RecruitmentStages_Returns_UnprocessableEntity_When_Name_Is_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages",
            new { companyId, name = string.Empty, displayOrder = 10, isTerminal = false, terminalOutcome = "None" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_RecruitmentStages_Returns_UnprocessableEntity_For_Duplicate_Name()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages",
            new { companyId, name = "Technical Test", displayOrder = 10, isTerminal = false, terminalOutcome = "None" });

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages",
            new { companyId, name = "Technical Test", displayOrder = 11, isTerminal = false, terminalOutcome = "None" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_RecruitmentStages_Returns_UnprocessableEntity_For_Duplicate_DisplayOrder()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages",
            new { companyId, name = "Technical Test", displayOrder = 10, isTerminal = false, terminalOutcome = "None" });

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages",
            new { companyId, name = "Second Interview", displayOrder = 10, isTerminal = false, terminalOutcome = "None" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_RecruitmentStages_Returns_UnprocessableEntity_When_Active_Hired_Stage_Already_Exists()
    {
        var companyId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            db.RecruitmentStages.AddRange(RecruitmentStageSeeder.BuildDefaultStages(companyId, Now));
            await db.SaveChangesAsync();
        }

        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/recruitment-stages",
            new { companyId, name = "Second Hired Stage", displayOrder = 20, isTerminal = true, terminalOutcome = "Hired" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record StagePayload(
        Guid Id, Guid CompanyId, string Name, int DisplayOrder, bool IsActive, bool IsTerminal,
        string TerminalOutcome, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
