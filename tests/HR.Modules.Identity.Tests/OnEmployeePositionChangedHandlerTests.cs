using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.OnEmployeePositionChanged;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class OnEmployeePositionChangedHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private Handler BuildHandler(
        FakeAuditEventPublisher auditPublisher,
        IPositionProfileReader? reader = null,
        IEmployeeAudienceReader? audienceReader = null)
    {
        var db = fixture.BuildContext();
        reader ??= new FakePositionProfileReader();
        // Defaults to "authoritative" being absent (matches nothing) — individual tests that care
        // about the authoritative-current-position guard supply their own audienceReader reflecting
        // the employee's true current position so the handler's mutation actually proceeds.
        audienceReader ??= new FakeEmployeeAudienceReader([]);
        return new Handler(db, Clock, auditPublisher, new PositionSync(db, reader), audienceReader, NullLogger<Handler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_Expires_Previous_Assignment_And_Creates_New_One_And_Publishes_Audit_Event()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var previousPositionId = Guid.NewGuid();
        var newPositionId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Positions.Add(Position.Create(previousPositionId, companyId, "Junior Developer", Now));
            db.PositionRoles.Add(PositionRole.Create(previousPositionId, SystemRoles.Employee, Now));
            db.UserPositions.Add(UserPosition.Create(employeeId, previousPositionId, Now.AddDays(-30)));
            await db.SaveChangesAsync();
        }

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [newPositionId] = new(newPositionId, "Senior Developer", null, null, true, null, null),
        };
        var reader = new FakePositionProfileReader(summaries: summaries);
        var audienceReader = new FakeEmployeeAudienceReader(
            [employeeId],
            audienceProfiles: new Dictionary<Guid, EmployeeAudienceProfile> { [employeeId] = new(null, null, newPositionId) });

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, reader, audienceReader);

        var later = Now.AddDays(1);
        await handler.HandleAsync(
            new EmployeePositionChangedIntegrationEvent(companyId, employeeId, previousPositionId, newPositionId, later),
            CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        var previousAssignment = await db2.UserPositions.SingleAsync(up => up.PositionId == previousPositionId);
        Assert.NotNull(previousAssignment.ExpiresAt);
        Assert.False(previousAssignment.IsActive(later));

        var newAssignment = await db2.UserPositions.SingleAsync(up => up.PositionId == newPositionId);
        Assert.Null(newAssignment.ExpiresAt);
        Assert.True(await db2.Positions.AnyAsync(p => p.Id == newPositionId));

        var audit = Assert.Single(auditPublisher.PublishedEvents, e => e is EmployeeInheritedRolesRecalculatedAuditEvent);
        var typed = Assert.IsType<EmployeeInheritedRolesRecalculatedAuditEvent>(audit);
        Assert.Equal(previousPositionId, typed.PreviousPositionId);
        Assert.Equal(newPositionId, typed.NewPositionId);
        Assert.Contains(SystemRoles.Employee, typed.BeforeRoleIds);
        Assert.Empty(typed.AfterRoleIds); // new position has no configured role defaults yet
    }

    [Fact]
    public async Task HandleAsync_Reopens_A_Previously_Expired_Assignment_Rather_Than_Duplicating_It()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var oldPositionId = Guid.NewGuid();
        var originalPositionId = Guid.NewGuid(); // employee is returning to this one

        await using (var db = fixture.BuildContext())
        {
            db.Positions.Add(Position.Create(oldPositionId, companyId, "Team Lead", Now));
            db.Positions.Add(Position.Create(originalPositionId, companyId, "Developer", Now));
            db.UserPositions.Add(UserPosition.Create(employeeId, oldPositionId, Now.AddDays(-60)));
            // Employee held `originalPositionId` before, then moved away (now expired).
            db.UserPositions.Add(UserPosition.Create(employeeId, originalPositionId, Now.AddDays(-120), Now.AddDays(-60)));
            await db.SaveChangesAsync();
        }

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [originalPositionId] = new(originalPositionId, "Developer", null, null, true, null, null),
        };
        var reader = new FakePositionProfileReader(summaries: summaries);
        var audienceReader = new FakeEmployeeAudienceReader(
            [employeeId],
            audienceProfiles: new Dictionary<Guid, EmployeeAudienceProfile> { [employeeId] = new(null, null, originalPositionId) });
        var handler = BuildHandler(new FakeAuditEventPublisher(), reader, audienceReader);

        await handler.HandleAsync(
            new EmployeePositionChangedIntegrationEvent(companyId, employeeId, oldPositionId, originalPositionId, Now),
            CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        var assignments = await db2.UserPositions
            .Where(up => up.UserId == employeeId && up.PositionId == originalPositionId)
            .ToListAsync();
        Assert.Single(assignments); // reopened, not duplicated
        Assert.Null(assignments[0].ExpiresAt);
    }

    [Fact]
    public async Task HandleAsync_Is_Idempotent_When_Previous_Assignment_Already_Expired()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var previousPositionId = Guid.NewGuid();
        var newPositionId = Guid.NewGuid();
        var expiredAt = Now.AddDays(-1);

        await using (var db = fixture.BuildContext())
        {
            db.Positions.Add(Position.Create(previousPositionId, companyId, "Old Role", Now));
            db.UserPositions.Add(UserPosition.Create(employeeId, previousPositionId, Now.AddDays(-30), expiredAt));
            await db.SaveChangesAsync();
        }

        var audienceReader = new FakeEmployeeAudienceReader(
            [employeeId],
            audienceProfiles: new Dictionary<Guid, EmployeeAudienceProfile> { [employeeId] = new(null, null, newPositionId) });
        var handler = BuildHandler(new FakeAuditEventPublisher(), audienceReader: audienceReader);

        await handler.HandleAsync(
            new EmployeePositionChangedIntegrationEvent(companyId, employeeId, previousPositionId, newPositionId, Now),
            CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        var previousAssignment = await db2.UserPositions.SingleAsync(up => up.PositionId == previousPositionId);
        Assert.Equal(expiredAt, previousAssignment.ExpiresAt); // left untouched, not re-set to `now`
    }

    [Fact]
    public async Task HandleAsync_Skips_Entirely_When_Events_New_Position_Does_Not_Match_Employees_Current_Position()
    {
        // Simulates a stale/out-of-order EmployeePositionChangedIntegrationEvent: the employee has
        // already transferred A -> B -> C (current authoritative position is C), and a delayed A -> B
        // event arrives after the B -> C event already applied. The event's NewPositionProfileId (B)
        // no longer matches the employee's current position (C), so the handler must not touch either
        // the previous (A) or "new" (B) assignment, and must not publish an audit event.
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionA = Guid.NewGuid();
        var positionB = Guid.NewGuid();
        var positionC = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Positions.Add(Position.Create(positionA, companyId, "Position A", Now));
            db.Positions.Add(Position.Create(positionC, companyId, "Position C", Now));
            // A is still active (the delayed event hasn't been processed yet); C is the real, current
            // active assignment already applied by the (later-arriving-but-earlier-processed) B->C event.
            db.UserPositions.Add(UserPosition.Create(employeeId, positionA, Now.AddDays(-30)));
            db.UserPositions.Add(UserPosition.Create(employeeId, positionC, Now));
            await db.SaveChangesAsync();
        }

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionB] = new(positionB, "Position B", null, null, true, null, null),
        };
        var reader = new FakePositionProfileReader(summaries: summaries);
        var audienceReader = new FakeEmployeeAudienceReader(
            [employeeId],
            audienceProfiles: new Dictionary<Guid, EmployeeAudienceProfile> { [employeeId] = new(null, null, positionC) });

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, reader, audienceReader);

        // Stale event: claims a transfer A -> B, but the employee's current position is already C.
        await handler.HandleAsync(
            new EmployeePositionChangedIntegrationEvent(companyId, employeeId, positionA, positionB, Now.AddMinutes(1)),
            CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        var assignmentA = await db2.UserPositions.SingleAsync(up => up.PositionId == positionA);
        Assert.Null(assignmentA.ExpiresAt); // NOT expired — the event was stale, so A is left untouched

        Assert.False(await db2.UserPositions.AnyAsync(up => up.PositionId == positionB)); // B never created/reopened

        var assignmentC = await db2.UserPositions.SingleAsync(up => up.PositionId == positionC);
        Assert.Null(assignmentC.ExpiresAt); // untouched, still the real current assignment

        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Skips_Entirely_When_Employees_Current_Position_Cannot_Be_Resolved()
    {
        // The audience reader returning a null profile (employee not found / transient read failure)
        // must be treated the same as a mismatch — no mutation, no audit event — rather than falling
        // back to trusting the event's own NewPositionProfileId.
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var previousPositionId = Guid.NewGuid();
        var newPositionId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Positions.Add(Position.Create(previousPositionId, companyId, "Previous Position", Now));
            db.UserPositions.Add(UserPosition.Create(employeeId, previousPositionId, Now.AddDays(-30)));
            await db.SaveChangesAsync();
        }

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [newPositionId] = new(newPositionId, "New Position", null, null, true, null, null),
        };
        var reader = new FakePositionProfileReader(summaries: summaries);
        // No audience profile registered for employeeId -> GetEmployeeAudienceAsync resolves null.
        var audienceReader = new FakeEmployeeAudienceReader([employeeId]);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, reader, audienceReader);

        await handler.HandleAsync(
            new EmployeePositionChangedIntegrationEvent(companyId, employeeId, previousPositionId, newPositionId, Now.AddMinutes(1)),
            CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        var previousAssignment = await db2.UserPositions.SingleAsync(up => up.PositionId == previousPositionId);
        Assert.Null(previousAssignment.ExpiresAt); // untouched

        Assert.False(await db2.UserPositions.AnyAsync(up => up.PositionId == newPositionId)); // never created

        Assert.Empty(auditPublisher.PublishedEvents);
    }
}
