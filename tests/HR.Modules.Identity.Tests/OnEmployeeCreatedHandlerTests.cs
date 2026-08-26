using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.OnEmployeeCreated;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class OnEmployeeCreatedHandlerTests(IdentityDatabaseFixture fixture)
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
    public async Task HandleAsync_Is_NoOp_When_PositionProfileId_Is_Null()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 1, 1), null, new DateOnly(2026, 4, 1)),
            CancellationToken.None);

        await using var db = fixture.BuildContext();
        Assert.False(await db.UserPositions.AnyAsync(up => up.UserId == employeeId));
        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Creates_UserPosition_And_Syncs_Position_And_Publishes_Audit_Event()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Software Developer", null, null, true, null, null),
        };
        var reader = new FakePositionProfileReader(summaries: summaries);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, reader);

        await handler.HandleAsync(
            new EmployeeCreatedIntegrationEvent(
                companyId, employeeId, new DateOnly(2026, 1, 1), null, new DateOnly(2026, 4, 1), positionProfileId),
            CancellationToken.None);

        await using var db = fixture.BuildContext();
        var userPosition = await db.UserPositions.SingleAsync(up => up.UserId == employeeId);
        Assert.Equal(positionProfileId, userPosition.PositionId);
        Assert.Null(userPosition.ExpiresAt);

        Assert.True(await db.Positions.AnyAsync(p => p.Id == positionProfileId));

        Assert.Single(auditPublisher.PublishedEvents, e => e is EmployeeInheritedRolesRecalculatedAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Is_Idempotent_On_Repeat_Delivery()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Software Developer", null, null, true, null, null),
        };
        var reader = new FakePositionProfileReader(summaries: summaries);

        var firstAuditPublisher = new FakeAuditEventPublisher();
        var firstHandler = BuildHandler(firstAuditPublisher, reader);
        var integrationEvent = new EmployeeCreatedIntegrationEvent(
            companyId, employeeId, new DateOnly(2026, 1, 1), null, new DateOnly(2026, 4, 1), positionProfileId);

        await firstHandler.HandleAsync(integrationEvent, CancellationToken.None);
        Assert.Single(firstAuditPublisher.PublishedEvents);

        var secondAuditPublisher = new FakeAuditEventPublisher();
        var secondHandler = BuildHandler(secondAuditPublisher, reader);
        await secondHandler.HandleAsync(integrationEvent, CancellationToken.None);

        // Idempotent — repeat delivery must not duplicate the assignment or re-publish the audit event.
        Assert.Empty(secondAuditPublisher.PublishedEvents);

        await using var db = fixture.BuildContext();
        Assert.Single(await db.UserPositions.Where(up => up.UserId == employeeId).ToListAsync());
    }
}
