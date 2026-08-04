using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Probation.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies that GenerateDueProbationReviewsJob creates the correct reviews
/// and transitions records when run against a real database.
/// </summary>
[Collection("Integration")]
public class ProbationDueReviewGenerationEndToEndTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("cccccccc-0000-0000-0000-000000000003");

    // 90-day probation starting 70 days ago.
    // ManagerCheckIn (day 30) due 40 days ago ✓
    // HrReview       (day 60) due 10 days ago ✓
    // FinalDecision  (day 90) due 20 days from now ✗
    private static readonly DateOnly StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-70));
    private static readonly DateOnly EndDate   = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20));

    public ProbationDueReviewGenerationEndToEndTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Job_Creates_ManagerCheckIn_And_HrReview_For_Past_Due_Dates()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(User1, companyId);

        var recordId = await CreateRecordAsync(client, companyId);

        await RunJobAsync();

        var reviews = await GetReviewsAsync(client, companyId, recordId);
        Assert.Equal(2, reviews.Count);
        Assert.Contains(reviews, r => r.ReviewType == "ManagerCheckIn" && r.Status == "Pending");
        Assert.Contains(reviews, r => r.ReviewType == "HrReview"       && r.Status == "Pending");
    }

    [Fact]
    public async Task Job_Does_Not_Create_FinalDecision_When_Not_Yet_Due()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(User2, companyId);

        var recordId = await CreateRecordAsync(client, companyId);

        await RunJobAsync();

        var reviews = await GetReviewsAsync(client, companyId, recordId);
        Assert.DoesNotContain(reviews, r => r.ReviewType == "FinalDecision");
    }

    [Fact]
    public async Task Job_Transitions_Active_Record_To_ReviewDue()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(User3, companyId);

        var recordId = await CreateRecordAsync(client, companyId);

        await RunJobAsync();

        var record = await GetRecordAsync(client, companyId, recordId);
        Assert.Equal("ReviewDue", record.Status);
    }

    [Fact]
    public async Task Job_Does_Not_Create_Duplicate_Reviews_On_Second_Run()
    {
        var companyId = Guid.NewGuid();
        var userId    = new Guid("cccccccc-0000-0000-0000-000000000004");
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await AuthenticatedClient(userId, companyId);

        var recordId = await CreateRecordAsync(client, companyId);

        await RunJobAsync();
        await RunJobAsync();

        var reviews = await GetReviewsAsync(client, companyId, recordId);
        Assert.Equal(2, reviews.Count);
        Assert.Single(reviews, r => r.ReviewType == "ManagerCheckIn");
        Assert.Single(reviews, r => r.ReviewType == "HrReview");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthenticatedClient(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private static async Task<Guid> CreateRecordAsync(HttpClient client, Guid companyId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records",
            new
            {
                companyId,
                employeeId        = Guid.NewGuid(),
                managerEmployeeId = Guid.NewGuid(),
                startDate         = StartDate.ToString("yyyy-MM-dd"),
                expectedEndDate   = EndDate.ToString("yyyy-MM-dd")
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<RecordPayload>();
        return payload!.Id;
    }

    private async Task RunJobAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<GenerateDueProbationReviewsJob>();
        await job.ExecuteAsync();
    }

    private static async Task<IReadOnlyList<ReviewItem>> GetReviewsAsync(
        HttpClient client, Guid companyId, Guid recordId)
    {
        var response = await client.GetAsync(
            $"/api/companies/{companyId}/probation-records/{recordId}/reviews");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ReviewListPayload>();
        return payload!.Items;
    }

    private static async Task<RecordDetailPayload> GetRecordAsync(
        HttpClient client, Guid companyId, Guid recordId)
    {
        var response = await client.GetAsync(
            $"/api/companies/{companyId}/probation-records/{recordId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecordDetailPayload>())!;
    }

    private sealed record RecordPayload(Guid Id);
    private sealed record RecordDetailPayload(Guid Id, string Status);
    private sealed record ReviewListPayload(IReadOnlyList<ReviewItem> Items);
    private sealed record ReviewItem(Guid Id, string ReviewType, string Status);
}
