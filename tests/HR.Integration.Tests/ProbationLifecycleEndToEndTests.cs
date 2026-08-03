using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Probation.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies the complete probation lifecycle as a single coherent flow:
///
///   Create record (all reviews past due)
///     → Run job → ManagerCheckIn, HrReview and FinalDecision tasks created for manager
///     → Manager completes ManagerCheckIn task
///     → Manager completes HrReview task
///     → Manager completes FinalDecision task with Pass outcome
///     → Record transitions to Passed
/// </summary>
[Collection("Integration")]
public class ProbationLifecycleEndToEndTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid AdminUser = new("b1b1b1b1-0000-0000-0000-000000000001");

    // 90-day probation that already ended 10 days ago: all three reviews are past due.
    private static readonly DateOnly ProbationStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-100));
    private static readonly DateOnly ProbationEnd   = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10));

    public ProbationLifecycleEndToEndTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Full_Probation_Lifecycle_From_Record_Creation_To_Passed_Outcome()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        using var client = AuthenticatedClient(AdminUser, companyId);

        // ── Step 1: Create probation record ───────────────────────────────────
        var recordResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records",
            new
            {
                companyId,
                employeeId,
                managerEmployeeId = managerId,
                startDate         = ProbationStart.ToString("yyyy-MM-dd"),
                expectedEndDate   = ProbationEnd.ToString("yyyy-MM-dd")
            });
        recordResp.EnsureSuccessStatusCode();
        var record = await recordResp.Content.ReadFromJsonAsync<IdPayload>();

        // ── Step 2: Run the generation job ────────────────────────────────────
        // All three review milestones are in the past, so all three are created.
        await RunGenerationJobAsync();

        // ── Step 3: Verify three reviews were created ──────────────────────────
        var reviews = await GetReviewsAsync(client, companyId, record!.Id);
        Assert.Equal(3, reviews.Count);
        Assert.Contains(reviews, r => r.ReviewType == "ManagerCheckIn" && r.Status == "Pending");
        Assert.Contains(reviews, r => r.ReviewType == "HrReview"       && r.Status == "Pending");
        Assert.Contains(reviews, r => r.ReviewType == "FinalDecision"  && r.Status == "Pending");

        // ── Step 4: Verify three tasks were created for the manager ───────────
        var managerTasks = await GetEmployeeTasksAsync(client, companyId, managerId);
        var probationTasks = managerTasks
            .Where(t => t.Source == "Probation" && t.ActionType == "Review")
            .ToList();
        Assert.Equal(3, probationTasks.Count);

        // ── Step 5: Complete ManagerCheckIn task ──────────────────────────────
        var checkInTask = probationTasks
            .Single(t => reviews.Any(r => r.ReviewType == "ManagerCheckIn" && r.Id == t.SourceEntityId));
        var checkInResp = await client.PostAsync(
            $"/api/companies/{companyId}/tasks/{checkInTask.Id}/complete",
            EmptyJson());
        checkInResp.EnsureSuccessStatusCode();

        var checkInReview = reviews.Single(r => r.ReviewType == "ManagerCheckIn");
        var checkInAfter = await GetReviewAsync(client, companyId, record.Id, checkInReview.Id);
        Assert.Equal("Completed", checkInAfter.Status);

        // ── Step 6: Complete HrReview task ────────────────────────────────────
        var hrReviewTask = probationTasks
            .Single(t => reviews.Any(r => r.ReviewType == "HrReview" && r.Id == t.SourceEntityId));
        var hrResp = await client.PostAsync(
            $"/api/companies/{companyId}/tasks/{hrReviewTask.Id}/complete",
            EmptyJson());
        hrResp.EnsureSuccessStatusCode();

        // ── Step 7: Complete FinalDecision task with Pass outcome ──────────────
        var finalTask = probationTasks
            .Single(t => reviews.Any(r => r.ReviewType == "FinalDecision" && r.Id == t.SourceEntityId));
        var finalResp = await client.PostAsync(
            $"/api/companies/{companyId}/tasks/{finalTask.Id}/complete",
            Json(new { outcomeDecision = "Pass", outcomeReason = "All objectives met." }));
        finalResp.EnsureSuccessStatusCode();

        // ── Step 8: Verify record has transitioned to Passed ──────────────────
        var finalRecord = await GetRecordAsync(client, companyId, record.Id);
        Assert.Equal("Passed", finalRecord.Status);

        // ── Step 9: Verify FinalDecision review shows Pass outcome ─────────────
        var finalDecisionReview = reviews.Single(r => r.ReviewType == "FinalDecision");
        var finalReviewAfter = await GetReviewAsync(client, companyId, record.Id, finalDecisionReview.Id);
        Assert.Equal("Completed", finalReviewAfter.Status);
        Assert.Equal("Pass", finalReviewAfter.Outcome);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private HttpClient AuthenticatedClient(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task RunGenerationJobAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<GenerateDueProbationReviewsJob>();
        await job.ExecuteAsync();
    }

    private static async Task<IReadOnlyList<ReviewItem>> GetReviewsAsync(
        HttpClient client, Guid companyId, Guid recordId)
    {
        var resp = await client.GetAsync(
            $"/api/companies/{companyId}/probation-records/{recordId}/reviews");
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<ReviewListPayload>();
        return payload!.Items;
    }

    private static async Task<ReviewItem> GetReviewAsync(
        HttpClient client, Guid companyId, Guid recordId, Guid reviewId)
    {
        var all = await GetReviewsAsync(client, companyId, recordId);
        return all.Single(r => r.Id == reviewId);
    }

    private static async Task<RecordDetailPayload> GetRecordAsync(
        HttpClient client, Guid companyId, Guid recordId)
    {
        var resp = await client.GetAsync(
            $"/api/companies/{companyId}/probation-records/{recordId}");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<RecordDetailPayload>())!;
    }

    private static async Task<List<TaskItem>> GetEmployeeTasksAsync(
        HttpClient client, Guid companyId, Guid employeeId)
    {
        var resp = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/tasks");
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<TaskListPayload>();
        return payload!.Items.ToList();
    }

    private static StringContent EmptyJson() =>
        new("{}", Encoding.UTF8, "application/json");

    private static StringContent Json(object payload) =>
        new(System.Text.Json.JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private sealed record IdPayload(Guid Id);
    private sealed record RecordDetailPayload(Guid Id, string Status);
    private sealed record ReviewListPayload(IReadOnlyList<ReviewItem> Items);
    private sealed record ReviewItem(Guid Id, string ReviewType, string Status, string? Outcome);
    private sealed record TaskListPayload(IReadOnlyList<TaskItem> Items);
    private sealed record TaskItem(Guid Id, string Source, string ActionType, string Status, Guid? SourceEntityId);
}
