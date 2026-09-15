using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Jobs;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Leave.Tests.Jobs;

// Daily sweep that re-enqueues any LeavePolicyDeactivationOnDeparture record still Pending or
// Failed — covers the case where the initial Hangfire enqueue never happened, or a Failed record
// needs a fresh round of retries.
public class ReconcileLeavePolicyDeactivationsJobTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 6, 8, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 8, 8, 0, 0, TimeSpan.Zero);

    private static LeaveDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static LeavePolicyDeactivationOnDeparture CreatePending(Guid companyId, Guid employeeId) =>
        LeavePolicyDeactivationOnDeparture.CreatePending(Guid.NewGuid(), companyId, employeeId, OccurredAt, RequestedAt);

    [Fact]
    public async Task ExecuteAsync_Is_NoOp_When_Nothing_Stuck()
    {
        await using var db = BuildContext();
        var jobClient = new RecordingBackgroundJobClient();
        var job = new ReconcileLeavePolicyDeactivationsJob(
            db, jobClient, NullLogger<ReconcileLeavePolicyDeactivationsJob>.Instance);

        var exception = await Record.ExceptionAsync(() => job.ExecuteAsync());

        Assert.Null(exception);
        Assert.Empty(jobClient.CreatedJobs);
    }

    [Fact]
    public async Task ExecuteAsync_ReEnqueues_Pending_Records()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var pendingRequest = CreatePending(companyId, Guid.NewGuid());
        db.LeavePolicyDeactivationsOnDeparture.Add(pendingRequest);
        await db.SaveChangesAsync();

        var jobClient = new RecordingBackgroundJobClient();
        var job = new ReconcileLeavePolicyDeactivationsJob(
            db, jobClient, NullLogger<ReconcileLeavePolicyDeactivationsJob>.Instance);

        await job.ExecuteAsync();

        Assert.Single(jobClient.CreatedJobs, j => j.Type == typeof(LeavePolicyDeactivationJob));

        var reloaded = await db.LeavePolicyDeactivationsOnDeparture.SingleAsync(r => r.Id == pendingRequest.Id);
        Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusPending, reloaded.Status); // unchanged
    }

    [Fact]
    public async Task ExecuteAsync_Resets_Failed_Records_To_Pending_Then_ReEnqueues()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var failedRequest = CreatePending(companyId, Guid.NewGuid());
        failedRequest.MarkProcessing(RequestedAt.AddMinutes(1));
        failedRequest.MarkFailed("boom", RequestedAt.AddMinutes(2));
        db.LeavePolicyDeactivationsOnDeparture.Add(failedRequest);
        await db.SaveChangesAsync();

        var jobClient = new RecordingBackgroundJobClient();
        var job = new ReconcileLeavePolicyDeactivationsJob(
            db, jobClient, NullLogger<ReconcileLeavePolicyDeactivationsJob>.Instance);

        await job.ExecuteAsync();

        var reloaded = await db.LeavePolicyDeactivationsOnDeparture.SingleAsync(r => r.Id == failedRequest.Id);
        Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusPending, reloaded.Status);
        Assert.Null(reloaded.FailureReason);

        Assert.Single(jobClient.CreatedJobs, j => j.Type == typeof(LeavePolicyDeactivationJob));
    }

    [Theory]
    [InlineData(LeavePolicyDeactivationOnDeparture.StatusProcessing)]
    [InlineData(LeavePolicyDeactivationOnDeparture.StatusProcessed)]
    public async Task ExecuteAsync_Ignores_Records_Not_Pending_Or_Failed(string status)
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = CreatePending(companyId, Guid.NewGuid());
        request.MarkProcessing(RequestedAt.AddMinutes(1));
        if (status == LeavePolicyDeactivationOnDeparture.StatusProcessed)
            request.MarkProcessed(RequestedAt.AddMinutes(2));
        db.LeavePolicyDeactivationsOnDeparture.Add(request);
        await db.SaveChangesAsync();

        var jobClient = new RecordingBackgroundJobClient();
        var job = new ReconcileLeavePolicyDeactivationsJob(
            db, jobClient, NullLogger<ReconcileLeavePolicyDeactivationsJob>.Instance);

        await job.ExecuteAsync();

        Assert.Empty(jobClient.CreatedJobs);
    }

    [Fact]
    public async Task ExecuteAsync_ReEnqueues_Both_Pending_And_Failed_In_The_Same_Run_Ignoring_Others()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var pendingRequest = CreatePending(companyId, Guid.NewGuid());

        var failedRequest = CreatePending(companyId, Guid.NewGuid());
        failedRequest.MarkProcessing(RequestedAt.AddMinutes(1));
        failedRequest.MarkFailed("boom", RequestedAt.AddMinutes(2));

        var processedRequest = CreatePending(companyId, Guid.NewGuid());
        processedRequest.MarkProcessing(RequestedAt.AddMinutes(1));
        processedRequest.MarkProcessed(RequestedAt.AddMinutes(2));

        db.LeavePolicyDeactivationsOnDeparture.AddRange(pendingRequest, failedRequest, processedRequest);
        await db.SaveChangesAsync();

        var jobClient = new RecordingBackgroundJobClient();
        var job = new ReconcileLeavePolicyDeactivationsJob(
            db, jobClient, NullLogger<ReconcileLeavePolicyDeactivationsJob>.Instance);

        await job.ExecuteAsync();

        Assert.Equal(2, jobClient.CreatedJobs.Count(j => j.Type == typeof(LeavePolicyDeactivationJob)));
    }
}
