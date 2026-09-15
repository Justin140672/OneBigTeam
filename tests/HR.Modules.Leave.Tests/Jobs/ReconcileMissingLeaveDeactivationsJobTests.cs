using HR.Modules.Employees.Contracts;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Jobs;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Leave.Tests.Jobs;

// Gap-2 reliability fix: recovers finalised departures Employees considers fully complete that
// somehow ended up with NO LeavePolicyDeactivationOnDeparture row at all (e.g.
// EmployeeDepartureFinalisedHandler threw before its own insert). ReconcileLeavePolicyDeactivationsJob
// only re-enqueues rows that already exist, so this job — driven by the cross-module
// IFinalisedEmployeeDeparturesReader contract rather than Leave's own table — is the only mechanism
// that can catch this class of gap.
public class ReconcileMissingLeaveDeactivationsJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

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

    [Fact]
    public async Task ExecuteAsync_CreatesAndEnqueuesRequest_ForFinalisedDepartureWithNoExistingRow_AndActiveAssignment()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var assignment = CreateActiveAssignment(companyId, employeeId);
        db.EmployeeLeavePolicyAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var reader = new FakeFinalisedEmployeeDeparturesReader();
        reader.Add(Departure(companyId, employeeId, Now.AddDays(-1)));

        var jobClient = new RecordingBackgroundJobClient();
        var job = new ReconcileMissingLeaveDeactivationsJob(
            db, reader, new FakeClock(Now.UtcDateTime), jobClient,
            NullLogger<ReconcileMissingLeaveDeactivationsJob>.Instance);

        await job.ExecuteAsync();

        var request = await db.LeavePolicyDeactivationsOnDeparture
            .SingleAsync(d => d.CompanyId == companyId && d.EmployeeId == employeeId);
        Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusPending, request.Status);

        Assert.Single(jobClient.CreatedJobs, j => j.Type == typeof(LeavePolicyDeactivationJob));
    }

    [Fact]
    public async Task ExecuteAsync_Skips_FinalisedDeparture_WithNoActiveAssignment()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        // No assignment at all.
        var reader = new FakeFinalisedEmployeeDeparturesReader();
        reader.Add(Departure(companyId, employeeId, Now.AddDays(-1)));

        var jobClient = new RecordingBackgroundJobClient();
        var job = new ReconcileMissingLeaveDeactivationsJob(
            db, reader, new FakeClock(Now.UtcDateTime), jobClient,
            NullLogger<ReconcileMissingLeaveDeactivationsJob>.Instance);

        await job.ExecuteAsync();

        Assert.Empty(await db.LeavePolicyDeactivationsOnDeparture.ToListAsync());
        Assert.Empty(jobClient.CreatedJobs);
    }

    [Fact]
    public async Task ExecuteAsync_Skips_FinalisedDeparture_WithInactiveAssignment()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var assignment = CreateActiveAssignment(companyId, employeeId);
        assignment.Deactivate(Now);
        db.EmployeeLeavePolicyAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var reader = new FakeFinalisedEmployeeDeparturesReader();
        reader.Add(Departure(companyId, employeeId, Now.AddDays(-1)));

        var jobClient = new RecordingBackgroundJobClient();
        var job = new ReconcileMissingLeaveDeactivationsJob(
            db, reader, new FakeClock(Now.UtcDateTime), jobClient,
            NullLogger<ReconcileMissingLeaveDeactivationsJob>.Instance);

        await job.ExecuteAsync();

        Assert.Empty(await db.LeavePolicyDeactivationsOnDeparture.ToListAsync());
        Assert.Empty(jobClient.CreatedJobs);
    }

    [Theory]
    [InlineData(LeavePolicyDeactivationOnDeparture.StatusPending)]
    [InlineData(LeavePolicyDeactivationOnDeparture.StatusProcessing)]
    [InlineData(LeavePolicyDeactivationOnDeparture.StatusProcessed)]
    [InlineData(LeavePolicyDeactivationOnDeparture.StatusFailed)]
    public async Task ExecuteAsync_Skips_FinalisedDeparture_ThatAlreadyHasARequestRow_RegardlessOfStatus(string status)
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var assignment = CreateActiveAssignment(companyId, employeeId);
        db.EmployeeLeavePolicyAssignments.Add(assignment);

        var existing = LeavePolicyDeactivationOnDeparture.CreatePending(
            Guid.NewGuid(), companyId, employeeId, Now.AddDays(-2), Now.AddDays(-2));
        switch (status)
        {
            case LeavePolicyDeactivationOnDeparture.StatusProcessing:
                existing.MarkProcessing(Now.AddDays(-1));
                break;
            case LeavePolicyDeactivationOnDeparture.StatusProcessed:
                existing.MarkProcessing(Now.AddDays(-1));
                existing.MarkProcessed(Now.AddDays(-1));
                break;
            case LeavePolicyDeactivationOnDeparture.StatusFailed:
                existing.MarkProcessing(Now.AddDays(-1));
                existing.MarkFailed("boom", Now.AddDays(-1));
                break;
        }
        db.LeavePolicyDeactivationsOnDeparture.Add(existing);
        await db.SaveChangesAsync();

        var reader = new FakeFinalisedEmployeeDeparturesReader();
        reader.Add(Departure(companyId, employeeId, Now.AddDays(-1)));

        var jobClient = new RecordingBackgroundJobClient();
        var job = new ReconcileMissingLeaveDeactivationsJob(
            db, reader, new FakeClock(Now.UtcDateTime), jobClient,
            NullLogger<ReconcileMissingLeaveDeactivationsJob>.Instance);

        await job.ExecuteAsync();

        var rows = await db.LeavePolicyDeactivationsOnDeparture
            .Where(d => d.CompanyId == companyId && d.EmployeeId == employeeId)
            .ToListAsync();
        Assert.Single(rows); // no duplicate created
        Assert.Empty(jobClient.CreatedJobs);
    }

    [Fact]
    public async Task ExecuteAsync_Skips_Departures_OutsideTheLookbackWindow()
    {
        // The job derives `since = now - 30 days` and calls the reader with that bound. The fake
        // reader itself applies the "FinalisationCompletedAt >= since" filter (mirroring the real
        // FinalisedEmployeeDeparturesReader's query), so a departure finalised well outside the
        // window is never even returned to the job — proving the job honours the lookback rather
        // than unconditionally processing everything the reader happens to hold.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var assignment = CreateActiveAssignment(companyId, employeeId);
        db.EmployeeLeavePolicyAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var reader = new FakeFinalisedEmployeeDeparturesReader();
        reader.Add(Departure(companyId, employeeId, Now.AddDays(-31))); // outside the 30-day lookback

        var jobClient = new RecordingBackgroundJobClient();
        var job = new ReconcileMissingLeaveDeactivationsJob(
            db, reader, new FakeClock(Now.UtcDateTime), jobClient,
            NullLogger<ReconcileMissingLeaveDeactivationsJob>.Instance);

        await job.ExecuteAsync();

        Assert.Empty(await db.LeavePolicyDeactivationsOnDeparture.ToListAsync());
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
        reader.Add(Departure(companyId, employeeId, Now.AddDays(-1)));

        var jobClient = new RecordingBackgroundJobClient();
        var job = new ReconcileMissingLeaveDeactivationsJob(
            db, reader, new FakeClock(Now.UtcDateTime), jobClient,
            NullLogger<ReconcileMissingLeaveDeactivationsJob>.Instance);

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        var rows = await db.LeavePolicyDeactivationsOnDeparture
            .Where(d => d.CompanyId == companyId && d.EmployeeId == employeeId)
            .ToListAsync();
        Assert.Single(rows);
        Assert.Single(jobClient.CreatedJobs, j => j.Type == typeof(LeavePolicyDeactivationJob));
    }

    [Fact]
    public async Task ExecuteAsync_MultipleDepartures_IsolatesEachOne_OneMissingOneAlreadyRequested()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var missingEmployeeId = Guid.NewGuid();
        var alreadyRequestedEmployeeId = Guid.NewGuid();

        db.EmployeeLeavePolicyAssignments.AddRange(
            CreateActiveAssignment(companyId, missingEmployeeId),
            CreateActiveAssignment(companyId, alreadyRequestedEmployeeId));

        db.LeavePolicyDeactivationsOnDeparture.Add(LeavePolicyDeactivationOnDeparture.CreatePending(
            Guid.NewGuid(), companyId, alreadyRequestedEmployeeId, Now.AddDays(-2), Now.AddDays(-2)));
        await db.SaveChangesAsync();

        var reader = new FakeFinalisedEmployeeDeparturesReader();
        reader.Add(Departure(companyId, missingEmployeeId, Now.AddDays(-1)));
        reader.Add(Departure(companyId, alreadyRequestedEmployeeId, Now.AddDays(-1)));

        var jobClient = new RecordingBackgroundJobClient();
        var job = new ReconcileMissingLeaveDeactivationsJob(
            db, reader, new FakeClock(Now.UtcDateTime), jobClient,
            NullLogger<ReconcileMissingLeaveDeactivationsJob>.Instance);

        await job.ExecuteAsync();

        Assert.Single(jobClient.CreatedJobs, j => j.Type == typeof(LeavePolicyDeactivationJob));

        var missingRows = await db.LeavePolicyDeactivationsOnDeparture
            .Where(d => d.EmployeeId == missingEmployeeId).ToListAsync();
        Assert.Single(missingRows);

        var alreadyRequestedRows = await db.LeavePolicyDeactivationsOnDeparture
            .Where(d => d.EmployeeId == alreadyRequestedEmployeeId).ToListAsync();
        Assert.Single(alreadyRequestedRows); // still just the one, not duplicated
    }
}
