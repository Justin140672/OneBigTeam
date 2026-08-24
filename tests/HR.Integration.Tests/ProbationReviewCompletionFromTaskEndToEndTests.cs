using HR.Modules.Tasks.Contracts;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies that completing a task whose Source is ProbationReview triggers
/// CompleteProbationReviewFromTaskAction and updates both the review and the record.
/// </summary>
[Collection("Integration")]
public class ProbationReviewCompletionFromTaskEndToEndTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("dddddddd-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("dddddddd-0000-0000-0000-000000000003");

    public ProbationReviewCompletionFromTaskEndToEndTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task CompleteTask_Marks_ManagerCheckIn_Review_As_Completed()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(User1, companyId);

        var (recordId, reviewId) = await CreateRecordAndReviewAsync(client, companyId, "ManagerCheckIn");

        var taskId = await TaskSeeder.SeedAsync(
            _factory, companyId,
            title: "Complete probation review — Test Employee",
            source: TaskSource.Probation,
            actionType: TaskActionType.Review,
            sourceEntityId: reviewId,
            assignedEmployeeId: Guid.NewGuid());

        var completeResponse = await client.PostAsync(
            $"/api/companies/{companyId}/tasks/{taskId}/complete",
            Json(new { outcomeDecision = (string?)null, outcomeReason = (string?)null }));
        completeResponse.EnsureSuccessStatusCode();

        var review = await GetSingleReviewAsync(client, companyId, recordId);
        Assert.Equal("Completed", review.Status);
        Assert.NotNull(review.CompletedAt);
    }

    [Fact]
    public async Task CompleteTask_With_Pass_Outcome_Transitions_FinalDecision_And_Record_To_Passed()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(User2, companyId);

        var (recordId, reviewId) = await CreateRecordAndReviewAsync(client, companyId, "FinalDecision");

        var taskId = await TaskSeeder.SeedAsync(
            _factory, companyId,
            title: "Complete probation review — Test Employee",
            source: TaskSource.Probation,
            actionType: TaskActionType.Review,
            sourceEntityId: reviewId,
            assignedEmployeeId: Guid.NewGuid());

        var completeResponse = await client.PostAsync(
            $"/api/companies/{companyId}/tasks/{taskId}/complete",
            Json(new { outcomeDecision = "Pass", outcomeReason = "All objectives met." }));
        completeResponse.EnsureSuccessStatusCode();

        var review = await GetSingleReviewAsync(client, companyId, recordId);
        Assert.Equal("Completed", review.Status);
        Assert.Equal("Pass", review.Outcome);

        var record = await GetRecordAsync(client, companyId, recordId);
        Assert.Equal("Passed", record.Status);
    }

    [Fact]
    public async Task CompleteTask_With_Fail_Outcome_Transitions_FinalDecision_And_Record_To_Failed()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(User3, companyId);

        var (recordId, reviewId) = await CreateRecordAndReviewAsync(client, companyId, "FinalDecision");

        var taskId = await TaskSeeder.SeedAsync(
            _factory, companyId,
            title: "Complete probation review — Test Employee",
            source: TaskSource.Probation,
            actionType: TaskActionType.Review,
            sourceEntityId: reviewId,
            assignedEmployeeId: Guid.NewGuid());

        var completeResponse = await client.PostAsync(
            $"/api/companies/{companyId}/tasks/{taskId}/complete",
            Json(new { outcomeDecision = "Fail", outcomeReason = "Did not meet targets." }));
        completeResponse.EnsureSuccessStatusCode();

        var review = await GetSingleReviewAsync(client, companyId, recordId);
        Assert.Equal("Completed", review.Status);
        Assert.Equal("Fail", review.Outcome);

        var record = await GetRecordAsync(client, companyId, recordId);
        Assert.Equal("Failed", record.Status);
    }

    /// <summary>
    /// PROB-05: a malformed/garbage OutcomeDecision string on a FinalDecision review completes the
    /// underlying Task (Tasks module completion itself is outside Probation's control) but the
    /// Probation review is left Pending and the record's status/outcome fields are left untouched.
    /// </summary>
    [Fact]
    public async Task CompleteTask_With_Malformed_OutcomeDecision_Completes_Task_But_Leaves_Review_Pending()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(User1, companyId);

        var (recordId, reviewId) = await CreateRecordAndReviewAsync(client, companyId, "FinalDecision");

        var taskId = await TaskSeeder.SeedAsync(
            _factory, companyId,
            title: "Complete probation review — Test Employee",
            source: TaskSource.Probation,
            actionType: TaskActionType.Review,
            sourceEntityId: reviewId,
            assignedEmployeeId: Guid.NewGuid());

        var completeResponse = await client.PostAsync(
            $"/api/companies/{companyId}/tasks/{taskId}/complete",
            Json(new { outcomeDecision = "garbage", outcomeReason = (string?)null }));
        completeResponse.EnsureSuccessStatusCode();

        var review = await GetSingleReviewAsync(client, companyId, recordId);
        Assert.Equal("Pending", review.Status);
        Assert.Null(review.CompletedAt);
        Assert.Null(review.Outcome);

        var record = await GetRecordAsync(client, companyId, recordId);
        Assert.Equal("Active", record.Status);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthenticatedClient(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private static async Task<(Guid recordId, Guid reviewId)> CreateRecordAndReviewAsync(
        HttpClient client, Guid companyId, string reviewType)
    {
        var recordResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records",
            new
            {
                companyId,
                employeeId        = Guid.NewGuid(),
                managerEmployeeId = Guid.NewGuid(),
                startDate         = "2026-01-01",
                expectedEndDate   = "2026-04-01"
            });
        recordResp.EnsureSuccessStatusCode();
        var record = await recordResp.Content.ReadFromJsonAsync<IdPayload>();

        var reviewResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-reviews",
            new
            {
                companyId,
                probationRecordId = record!.Id,
                reviewType,
                dueDate = "2026-02-01"
            });
        reviewResp.EnsureSuccessStatusCode();
        var review = await reviewResp.Content.ReadFromJsonAsync<IdPayload>();

        return (record.Id, review!.Id);
    }

    private static async Task<ReviewDetailPayload> GetSingleReviewAsync(
        HttpClient client, Guid companyId, Guid recordId)
    {
        var response = await client.GetAsync(
            $"/api/companies/{companyId}/probation-records/{recordId}/reviews");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ReviewListPayload>();
        return payload!.Items.Single();
    }

    private static async Task<RecordDetailPayload> GetRecordAsync(
        HttpClient client, Guid companyId, Guid recordId)
    {
        var response = await client.GetAsync(
            $"/api/companies/{companyId}/probation-records/{recordId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecordDetailPayload>())!;
    }

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private sealed record IdPayload(Guid Id);
    private sealed record RecordDetailPayload(Guid Id, string Status);
    private sealed record ReviewListPayload(IReadOnlyList<ReviewDetailPayload> Items);
    private sealed record ReviewDetailPayload(
        Guid Id,
        string ReviewType,
        string Status,
        string? Outcome,
        DateTimeOffset? CompletedAt);
}
