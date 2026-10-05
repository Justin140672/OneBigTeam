using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class AddInterviewStageEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000a7-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc0000a7-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public AddInterviewStageEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<List<StageRow>> LoadStagesAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        return await db.RecruitmentStages.AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .OrderBy(s => s.DisplayOrder)
            .Select(s => new StageRow(s.Id, s.Name, s.DisplayOrder, s.IsActive, s.IsTerminal, s.Purpose))
            .ToListAsync();
    }

    private static string Url(Guid companyId) => $"/api/companies/{companyId}/recruitment-stages/interview-stages";

    private static string SuggestionUrl(Guid companyId) => $"/api/companies/{companyId}/recruitment-stages/interview-stage-suggestion";

    [Fact]
    public async Task Post_InterviewStages_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(Url(Guid.NewGuid()), new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_InterviewStages_Returns_Forbidden_Without_Recruitment_Management()
    {
        var companyId = Guid.NewGuid();
        await SeedDefaultStagesAsync(companyId);
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.PostAsJsonAsync(Url(companyId), new { companyId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(6, (await LoadStagesAsync(companyId)).Count);
    }

    [Fact]
    public async Task Post_InterviewStages_Returns_Forbidden_For_Another_Companys_Route()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        await SeedDefaultStagesAsync(otherCompanyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(Url(otherCompanyId), new { companyId = otherCompanyId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(6, (await LoadStagesAsync(otherCompanyId)).Count);
    }

    [Fact]
    public async Task Post_InterviewStages_Adds_Second_Interview_And_Renames_Default_Atomically()
    {
        var companyId = Guid.NewGuid();
        await SeedDefaultStagesAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(Url(companyId), new { companyId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AddPayload>();
        Assert.Equal("Second Interview", payload!.Name);
        Assert.Equal(4, payload.DisplayOrder);
        Assert.NotNull(payload.RenamedStageId);

        var stages = await LoadStagesAsync(companyId);
        Assert.Equal(
            ["Application Received", "CV Review", "First Interview", "Second Interview", "Offer", "Hired", "Rejected"],
            stages.Select(s => s.Name));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], stages.Select(s => s.DisplayOrder));
        var added = stages.Single(s => s.Name == "Second Interview");
        Assert.Equal(RecruitmentStagePurpose.Interview, added.Purpose);
        Assert.True(added.IsActive);
        Assert.False(added.IsTerminal);
    }

    [Fact]
    public async Task Post_InterviewStages_Third_Is_Named_Third_Interview_And_Inserted_After_Second()
    {
        var companyId = Guid.NewGuid();
        await SeedDefaultStagesAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        await client.PostAsJsonAsync(Url(companyId), new { companyId });
        var response = await client.PostAsJsonAsync(Url(companyId), new { companyId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AddPayload>();
        Assert.Equal("Third Interview", payload!.Name);
        Assert.Null(payload.RenamedStageId);

        var stages = await LoadStagesAsync(companyId);
        Assert.Equal(
            ["Application Received", "CV Review", "First Interview", "Second Interview", "Third Interview", "Offer", "Hired", "Rejected"],
            stages.Select(s => s.Name));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], stages.Select(s => s.DisplayOrder));
    }

    [Fact]
    public async Task Post_InterviewStages_Uses_Name_Override_And_Preserves_Custom_Existing_Name()
    {
        var companyId = Guid.NewGuid();
        await SeedDefaultStagesAsync(companyId);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var interview = await db.RecruitmentStages.SingleAsync(s => s.CompanyId == companyId && s.Name == "Interview");
            interview.UpdateDetails("Technical Interview", false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
            await db.SaveChangesAsync();
        }
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(Url(companyId), new { companyId, name = "Hiring Manager Interview" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stages = await LoadStagesAsync(companyId);
        Assert.Contains(stages, s => s.Name == "Technical Interview");
        Assert.Contains(stages, s => s.Name == "Hiring Manager Interview" && s.DisplayOrder == 4);
    }

    [Fact]
    public async Task Post_InterviewStages_Returns_UnprocessableEntity_For_Duplicate_Name_And_Changes_Nothing()
    {
        var companyId = Guid.NewGuid();
        await SeedDefaultStagesAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(Url(companyId), new { companyId, name = "offer" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var stages = await LoadStagesAsync(companyId);
        Assert.Equal(6, stages.Count);
        Assert.Contains(stages, s => s.Name == "Interview");
    }

    [Fact]
    public async Task Post_InterviewStages_Rejects_Blank_Name()
    {
        var companyId = Guid.NewGuid();
        await SeedDefaultStagesAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(Url(companyId), new { companyId, name = "   " });

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
        Assert.Equal(6, (await LoadStagesAsync(companyId)).Count);
    }

    [Fact]
    public async Task Post_InterviewStages_Ignores_Inactive_Interview_Stages_And_Other_Companies()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        await SeedDefaultStagesAsync(companyId);
        await SeedDefaultStagesAsync(otherCompanyId);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var inactive = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Old Interview", 7, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
            inactive.SetActiveStatus(false, Now);
            db.RecruitmentStages.Add(inactive);
            await db.SaveChangesAsync();
        }
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(Url(companyId), new { companyId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Second Interview", (await response.Content.ReadFromJsonAsync<AddPayload>())!.Name);
        var other = await LoadStagesAsync(otherCompanyId);
        Assert.Equal(["Application Received", "CV Review", "Interview", "Offer", "Hired", "Rejected"], other.Select(s => s.Name));
        Assert.Equal([1, 2, 3, 4, 5, 6], other.Select(s => s.DisplayOrder));
    }

    [Fact]
    public async Task Get_InterviewStageSuggestion_Returns_Second_Interview_For_Default_Pipeline_Without_Changing_Data()
    {
        var companyId = Guid.NewGuid();
        await SeedDefaultStagesAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync(SuggestionUrl(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SuggestionPayload>();
        Assert.Equal("Second Interview", payload!.SuggestedName);
        Assert.Equal(4, payload.DisplayOrder);
        Assert.Equal(1, payload.ActiveInterviewStageCount);
        Assert.Equal("First Interview", payload.RenamedStageName);

        var stages = await LoadStagesAsync(companyId);
        Assert.Equal(6, stages.Count);
        Assert.Contains(stages, s => s.Name == "Interview");
    }

    [Fact]
    public async Task Get_InterviewStageSuggestion_Returns_Unauthorized_For_Anonymous_And_Forbidden_Without_Permission()
    {
        var companyId = Guid.NewGuid();
        using var anonymous = _factory.CreateClient();
        var anonymousResponse = await anonymous.GetAsync(SuggestionUrl(companyId));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var client = await ClientAs(PlainEmployeeUser, companyId);
        var response = await client.GetAsync(SuggestionUrl(companyId));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record StageRow(Guid Id, string Name, int DisplayOrder, bool IsActive, bool IsTerminal, RecruitmentStagePurpose? Purpose);
    private sealed record AddPayload(Guid Id, string Name, int DisplayOrder, bool IsActive, Guid? RenamedStageId, string? RenamedStageName);
    private sealed record SuggestionPayload(string SuggestedName, int DisplayOrder, int ActiveInterviewStageCount, string? StageToRenameName, string? RenamedStageName);
}
