using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.OnEmployeePositionChanged;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class OnEmployeePositionChangedHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private Handler BuildHandler(FakeAuditEventPublisher auditPublisher, IPositionProfileReader? reader = null)
    {
        var db = fixture.BuildContext();
        reader ??= new FakePositionProfileReader();
        return new Handler(db, Clock, auditPublisher, new PositionSync(db, reader));
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

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, reader);

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
        var handler = BuildHandler(new FakeAuditEventPublisher(), reader);

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

        var handler = BuildHandler(new FakeAuditEventPublisher());

        await handler.HandleAsync(
            new EmployeePositionChangedIntegrationEvent(companyId, employeeId, previousPositionId, newPositionId, Now),
            CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        var previousAssignment = await db2.UserPositions.SingleAsync(up => up.PositionId == previousPositionId);
        Assert.Equal(expiredAt, previousAssignment.ExpiresAt); // left untouched, not re-set to `now`
    }
}
