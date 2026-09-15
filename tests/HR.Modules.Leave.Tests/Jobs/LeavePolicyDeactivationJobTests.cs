using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Jobs;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Leave.Tests.Jobs;

// Reliability follow-up: unit tests for LeavePolicyDeactivationJob, the durable, retryable worker
// that performs and confirms the actual EmployeeLeavePolicyAssignment deactivation requested by
// EmployeeDepartureFinalisedHandler. Mirrors HR.Modules.Identity.Tests.Jobs.AccountDisablementJobTests.
public class LeavePolicyDeactivationJobTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 6, 8, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 8, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 6, 9, 9, 0, 0, TimeSpan.Zero);

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }

    private static LeavePolicyDeactivationJob BuildJob(LeaveDbContext db, HR.SharedKernel.IClock clock) =>
        new(db, clock, NullLogger<LeavePolicyDeactivationJob>.Instance);

    private static async Task<(Guid CompanyId, Guid EmployeeId, LeavePolicyDeactivationOnDeparture Request)>
        SeedPendingRequestWithAssignmentAsync(LeaveDbContext seedDb, bool assignmentActive = true)
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var assignment = EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), new DateOnly(2026, 1, 1), RequestedAt);
        if (!assignmentActive)
            assignment.Deactivate(RequestedAt);
        seedDb.EmployeeLeavePolicyAssignments.Add(assignment);

        var request = LeavePolicyDeactivationOnDeparture.CreatePending(
            Guid.NewGuid(), companyId, employeeId, OccurredAt, RequestedAt);
        seedDb.LeavePolicyDeactivationsOnDeparture.Add(request);

        await seedDb.SaveChangesAsync();
        return (companyId, employeeId, request);
    }

    [Fact]
    public async Task ProcessAsync_Happy_Path_Deactivates_Assignment_Using_Requests_OccurredAt_And_Marks_Processed()
    {
        await using var seedDb = BuildContext();
        var (companyId, employeeId, request) = await SeedPendingRequestWithAssignmentAsync(seedDb);

        var job = BuildJob(seedDb, new FakeClock(Now.UtcDateTime));

        await job.ProcessAsync(request.Id, companyId);

        var reloadedAssignment = await seedDb.EmployeeLeavePolicyAssignments.SingleAsync(a => a.EmployeeId == employeeId);
        Assert.False(reloadedAssignment.IsActive);
        Assert.Equal(OccurredAt, reloadedAssignment.DeactivatedAt);

        var reloadedRequest = await seedDb.LeavePolicyDeactivationsOnDeparture.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusProcessed, reloadedRequest.Status);
        Assert.Equal(Now, reloadedRequest.ProcessedAt);
    }

    [Fact]
    public async Task ProcessAsync_Does_Not_Throw_When_Request_Row_Is_Missing()
    {
        await using var db = BuildContext();
        var job = BuildJob(db, new FakeClock(Now.UtcDateTime));

        var exception = await Record.ExceptionAsync(() => job.ProcessAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ProcessAsync_Throws_When_Supplied_CompanyId_Does_Not_Match_Request()
    {
        await using var db = BuildContext();
        var (companyId, _, request) = await SeedPendingRequestWithAssignmentAsync(db);
        var otherCompanyId = Guid.NewGuid();
        Assert.NotEqual(companyId, otherCompanyId);

        var job = BuildJob(db, new FakeClock(Now.UtcDateTime));

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ProcessAsync(request.Id, otherCompanyId));
    }

    [Fact]
    public async Task ProcessAsync_Is_NoOp_When_Already_Processed()
    {
        await using var db = BuildContext();
        var (companyId, employeeId, request) = await SeedPendingRequestWithAssignmentAsync(db);
        request.MarkProcessing(RequestedAt.AddMinutes(1));
        request.MarkProcessed(RequestedAt.AddMinutes(2));
        await db.SaveChangesAsync();

        var job = BuildJob(db, new FakeClock(Now.UtcDateTime));
        await job.ProcessAsync(request.Id, companyId);

        var reloaded = await db.LeavePolicyDeactivationsOnDeparture.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(RequestedAt.AddMinutes(2), reloaded.ProcessedAt); // untouched — no second run occurred

        // Assignment was never actually deactivated by this no-op run, only by the earlier
        // (simulated) successful run this test doesn't perform — assignment remains untouched too.
        var assignment = await db.EmployeeLeavePolicyAssignments.SingleAsync(a => a.EmployeeId == employeeId);
        Assert.True(assignment.IsActive);
    }

    [Fact]
    public async Task ProcessAsync_Treated_As_Done_When_Assignment_Is_Missing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        // No EmployeeLeavePolicyAssignment seeded — assignment.Deactivate() is a null-conditional
        // no-op in this case, so the request should still be marked Processed rather than failing.
        var request = LeavePolicyDeactivationOnDeparture.CreatePending(
            Guid.NewGuid(), companyId, employeeId, OccurredAt, RequestedAt);
        db.LeavePolicyDeactivationsOnDeparture.Add(request);
        await db.SaveChangesAsync();

        var job = BuildJob(db, new FakeClock(Now.UtcDateTime));

        var exception = await Record.ExceptionAsync(() => job.ProcessAsync(request.Id, companyId));

        Assert.Null(exception);
        var reloaded = await db.LeavePolicyDeactivationsOnDeparture.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusProcessed, reloaded.Status);
    }

    [Fact]
    public async Task ProcessAsync_Already_Inactive_Assignment_Still_Marks_Processed_Without_Double_Deactivating()
    {
        await using var db = BuildContext();
        var (companyId, employeeId, request) = await SeedPendingRequestWithAssignmentAsync(db, assignmentActive: false);

        var job = BuildJob(db, new FakeClock(Now.UtcDateTime));
        await job.ProcessAsync(request.Id, companyId);

        var reloadedAssignment = await db.EmployeeLeavePolicyAssignments.SingleAsync(a => a.EmployeeId == employeeId);
        Assert.False(reloadedAssignment.IsActive);
        // Deactivate() is a no-op if already inactive, so DeactivatedAt keeps the original value
        // from SeedPendingRequestWithAssignmentAsync (RequestedAt), not the job's OccurredAt.
        Assert.Equal(RequestedAt, reloadedAssignment.DeactivatedAt);

        var reloadedRequest = await db.LeavePolicyDeactivationsOnDeparture.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusProcessed, reloadedRequest.Status);
    }

    // Fault injection: IClock is the only collaborator inside the try block that can be made to
    // fail without corrupting the DbContext directly. The 1st access (before the try, for
    // MarkProcessing) is left to succeed; the 2nd (fetching processedAt at the end of the try) is
    // made to throw, simulating a genuine mid-operation failure before the row is marked Processed.
    [Fact]
    public async Task ProcessAsync_Transient_Failure_Leaves_Processing_And_Rethrows_When_Retries_Remain()
    {
        await using var db = BuildContext();
        var (companyId, _, request) = await SeedPendingRequestWithAssignmentAsync(db);

        var throwingClock = new SelectiveThrowingClock(Now.UtcDateTime, throwOnCallNumber: 2);
        var job = new LeavePolicyDeactivationJob(db, throwingClock, NullLogger<LeavePolicyDeactivationJob>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ProcessAsync(request.Id, companyId));

        var reloaded = await db.LeavePolicyDeactivationsOnDeparture.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(1, reloaded.AttemptCount);
        Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusProcessing, reloaded.Status);
        Assert.Null(reloaded.FailureReason);
    }

    [Fact]
    public async Task ProcessAsync_Final_Attempt_Failure_Marks_Failed_With_Reason_And_Rethrows()
    {
        await using var db = BuildContext();
        var (companyId, _, request) = await SeedPendingRequestWithAssignmentAsync(db);

        // Seed AttemptCount directly to MaxAttempts-1 via the domain's own MarkProcessing, then
        // trigger exactly one more (final) failing attempt — mirrors AccountDisablementJobTests's
        // approach to avoid the Status==Processed short-circuit masking further attempts.
        for (var i = 0; i < LeavePolicyDeactivationJob.MaxAttempts - 1; i++)
            request.MarkProcessing(RequestedAt.AddMinutes(i + 1));
        await db.SaveChangesAsync();

        var throwingClock = new SelectiveThrowingClock(Now.UtcDateTime, throwOnCallNumber: 2);
        var job = new LeavePolicyDeactivationJob(db, throwingClock, NullLogger<LeavePolicyDeactivationJob>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ProcessAsync(request.Id, companyId));

        var reloaded = await db.LeavePolicyDeactivationsOnDeparture.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(LeavePolicyDeactivationJob.MaxAttempts, reloaded.AttemptCount);
        Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusFailed, reloaded.Status);
        Assert.Equal("Leave policy deactivation failed.", reloaded.FailureReason);
        Assert.NotNull(reloaded.LastAttemptAt);
    }
}
