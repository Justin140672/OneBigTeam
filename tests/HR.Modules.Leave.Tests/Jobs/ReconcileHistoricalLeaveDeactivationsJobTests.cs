using HR.Modules.Employees.Contracts;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Jobs;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Leave.Tests.Jobs;

// Round 3 reliability fix (Gap-2 follow-up): ReconcileMissingLeaveDeactivationsJob's 30-day lookback
// can never see a stranded departure finalised further back than that — this job walks the ENTIRE
// finalised-departure history in bounded, paginated batches, resuming from a durably persisted
// cursor, and is rehire-safe (skips employees who are current/non-former again).
public class ReconcileHistoricalLeaveDeactivationsJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 9, 0, 0, TimeSpan.Zero);

    private static LeaveDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static EmployeeLeavePolicyAssignment CreateActiveAssignment(Guid companyId, Guid employeeId) =>
        EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), new DateOnly(2020, 1, 1), Now);

    private static FinalisedEmployeeDeparture Departure(
        Guid companyId, Guid employeeId, DateTimeOffset finalisationCompletedAt) =>
        new(companyId, employeeId, Guid.NewGuid(), new DateOnly(2026, 6, 30), finalisationCompletedAt);

    private static ReconcileHistoricalLeaveDeactivationsJob BuildJob(
        LeaveDbContext db,
        FakeFinalisedEmployeeDeparturesReader reader,
        RecordingBackgroundJobClient jobClient,
        FakeCurrentEmployeeReader? currentEmployeeReader = null) =>
        new(db, reader, currentEmployeeReader ?? new FakeCurrentEmployeeReader(), new FakeClock(Now.UtcDateTime),
            jobClient, NullLogger<ReconcileHistoricalLeaveDeactivationsJob>.Instance);

    [Fact]
    public async Task ExecuteAsync_Repairs_Departure_Finalised_More_Than_30_Days_Ago()
    {
        // The exact case ReconcileMissingLeaveDeactivationsJob's 30-day lookback can never reach.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var assignment = CreateActiveAssignment(companyId, employeeId);
        db.EmployeeLeavePolicyAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var reader = new FakeFinalisedEmployeeDeparturesReader();
        reader.Add(Departure(companyId, employeeId, Now.AddDays(-400)));

        var jobClient = new RecordingBackgroundJobClient();
        var job = BuildJob(db, reader, jobClient);

        await job.ExecuteAsync();

        var request = await db.LeavePolicyDeactivationsOnDeparture
            .SingleAsync(d => d.CompanyId == companyId && d.EmployeeId == employeeId);
        Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusPending, request.Status);
        Assert.Single(jobClient.CreatedJobs, j => j.Type == typeof(LeavePolicyDeactivationJob));

        var progress = await db.HistoricalLeaveDeactivationRepairProgress.SingleAsync();
        Assert.True(progress.IsComplete); // page returned fewer than BatchSize -> sweep complete
        Assert.Equal(1, progress.TotalRepaired);
    }

    [Fact]
    public async Task ExecuteAsync_Skips_Rehired_Employee_Even_Though_Historical_Departure_Exists()
    {
        // Requirement 6: rehire safety. A current/non-former employee's legitimate, active
        // post-rehire assignment must not be deactivated based on a stale historical departure.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var assignment = CreateActiveAssignment(companyId, employeeId);
        db.EmployeeLeavePolicyAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var reader = new FakeFinalisedEmployeeDeparturesReader();
        reader.Add(Departure(companyId, employeeId, Now.AddDays(-400)));

        var currentEmployeeReader = new FakeCurrentEmployeeReader([employeeId]); // rehired: current again
        var jobClient = new RecordingBackgroundJobClient();
        var job = BuildJob(db, reader, jobClient, currentEmployeeReader);

        await job.ExecuteAsync();

        Assert.Empty(await db.LeavePolicyDeactivationsOnDeparture.ToListAsync());
        Assert.Empty(jobClient.CreatedJobs);

        var stillActive = await db.EmployeeLeavePolicyAssignments.SingleAsync(a => a.Id == assignment.Id);
        Assert.True(stillActive.IsActive);
    }

    [Fact]
    public async Task ExecuteAsync_Skips_Departure_WithNoActiveAssignment()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var reader = new FakeFinalisedEmployeeDeparturesReader();
        reader.Add(Departure(companyId, employeeId, Now.AddDays(-400)));

        var jobClient = new RecordingBackgroundJobClient();
        var job = BuildJob(db, reader, jobClient);

        await job.ExecuteAsync();

        Assert.Empty(await db.LeavePolicyDeactivationsOnDeparture.ToListAsync());
        Assert.Empty(jobClient.CreatedJobs);
    }

    [Fact]
    public async Task ExecuteAsync_Skips_Departure_ThatAlreadyHasARequestRow()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var assignment = CreateActiveAssignment(companyId, employeeId);
        db.EmployeeLeavePolicyAssignments.Add(assignment);

        var existing = LeavePolicyDeactivationOnDeparture.CreatePending(
            Guid.NewGuid(), companyId, employeeId, Now.AddDays(-400), Now.AddDays(-400));
        db.LeavePolicyDeactivationsOnDeparture.Add(existing);
        await db.SaveChangesAsync();

        var reader = new FakeFinalisedEmployeeDeparturesReader();
        reader.Add(Departure(companyId, employeeId, Now.AddDays(-400)));

        var jobClient = new RecordingBackgroundJobClient();
        var job = BuildJob(db, reader, jobClient);

        await job.ExecuteAsync();

        var rows = await db.LeavePolicyDeactivationsOnDeparture
            .Where(d => d.CompanyId == companyId && d.EmployeeId == employeeId)
            .ToListAsync();
        Assert.Single(rows); // no duplicate
        Assert.Empty(jobClient.CreatedJobs);
    }

    [Fact]
    public async Task ExecuteAsync_RunTwice_DoesNotCreateADuplicateRequest()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var assignment = CreateActiveAssignment(companyId, employeeId);
        db.EmployeeLeavePolicyAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var reader = new FakeFinalisedEmployeeDeparturesReader();
        reader.Add(Departure(companyId, employeeId, Now.AddDays(-400)));

        var jobClient = new RecordingBackgroundJobClient();

        await BuildJob(db, reader, jobClient).ExecuteAsync();
        // Second run: the first run already marked the sweep complete (single small page), so this
        // must be a cheap permanent no-op — the defining behaviour of IsComplete.
        await BuildJob(db, reader, jobClient).ExecuteAsync();

        var rows = await db.LeavePolicyDeactivationsOnDeparture
            .Where(d => d.CompanyId == companyId && d.EmployeeId == employeeId)
            .ToListAsync();
        Assert.Single(rows);
        Assert.Single(jobClient.CreatedJobs, j => j.Type == typeof(LeavePolicyDeactivationJob));

        var progress = await db.HistoricalLeaveDeactivationRepairProgress.SingleAsync();
        Assert.True(progress.IsComplete);
    }

    [Fact]
    public async Task ExecuteAsync_Already_Complete_Progress_Is_A_Permanent_NoOp()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var assignment = CreateActiveAssignment(companyId, employeeId);
        db.EmployeeLeavePolicyAssignments.Add(assignment);

        var progress = HistoricalLeaveDeactivationRepairProgressTestHelper.CreateComplete(Now);
        db.HistoricalLeaveDeactivationRepairProgress.Add(progress);
        await db.SaveChangesAsync();

        var reader = new FakeFinalisedEmployeeDeparturesReader();
        reader.Add(Departure(companyId, employeeId, Now.AddDays(-400)));

        var jobClient = new RecordingBackgroundJobClient();
        var job = BuildJob(db, reader, jobClient);

        await job.ExecuteAsync();

        // Even though a "missing" departure exists in the reader, the sweep is already marked
        // complete and must never process anything further.
        Assert.Empty(await db.LeavePolicyDeactivationsOnDeparture.ToListAsync());
        Assert.Empty(jobClient.CreatedJobs);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleDepartures_IsolatesEachOne()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var missingEmployeeId = Guid.NewGuid();
        var rehiredEmployeeId = Guid.NewGuid();
        var alreadyRequestedEmployeeId = Guid.NewGuid();

        db.EmployeeLeavePolicyAssignments.AddRange(
            CreateActiveAssignment(companyId, missingEmployeeId),
            CreateActiveAssignment(companyId, rehiredEmployeeId),
            CreateActiveAssignment(companyId, alreadyRequestedEmployeeId));

        db.LeavePolicyDeactivationsOnDeparture.Add(LeavePolicyDeactivationOnDeparture.CreatePending(
            Guid.NewGuid(), companyId, alreadyRequestedEmployeeId, Now.AddDays(-400), Now.AddDays(-400)));
        await db.SaveChangesAsync();

        var reader = new FakeFinalisedEmployeeDeparturesReader();
        reader.Add(Departure(companyId, missingEmployeeId, Now.AddDays(-400)));
        reader.Add(Departure(companyId, rehiredEmployeeId, Now.AddDays(-390)));
        reader.Add(Departure(companyId, alreadyRequestedEmployeeId, Now.AddDays(-380)));

        var currentEmployeeReader = new FakeCurrentEmployeeReader([rehiredEmployeeId]);
        var jobClient = new RecordingBackgroundJobClient();
        var job = BuildJob(db, reader, jobClient, currentEmployeeReader);

        await job.ExecuteAsync();

        Assert.Single(jobClient.CreatedJobs, j => j.Type == typeof(LeavePolicyDeactivationJob));

        Assert.Single(await db.LeavePolicyDeactivationsOnDeparture
            .Where(d => d.EmployeeId == missingEmployeeId).ToListAsync());
        Assert.Empty(await db.LeavePolicyDeactivationsOnDeparture
            .Where(d => d.EmployeeId == rehiredEmployeeId).ToListAsync());
        Assert.Single(await db.LeavePolicyDeactivationsOnDeparture
            .Where(d => d.EmployeeId == alreadyRequestedEmployeeId).ToListAsync());
    }
}

/// <summary>Test-only access to HistoricalLeaveDeactivationRepairProgress's internal factory, since
/// the job type itself only ever creates one via CreateNew — tests need to seed an already-complete
/// row directly to exercise the "permanent no-op" path without needing 200+ departure rows.</summary>
internal static class HistoricalLeaveDeactivationRepairProgressTestHelper
{
    public static HistoricalLeaveDeactivationRepairProgress CreateComplete(DateTimeOffset now)
    {
        var progress = HistoricalLeaveDeactivationRepairProgress.CreateNew(now);
        progress.MarkComplete(now);
        return progress;
    }
}
