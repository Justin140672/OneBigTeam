using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.OnOffboardingPlanCompleted;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class OnOffboardingPlanCompletedHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private Handler BuildHandler(FakeAuditEventPublisher auditPublisher) =>
        new(fixture.BuildContext(), Clock, auditPublisher);

    [Fact]
    public async Task HandleAsync_Disables_Active_User_And_Publishes_Audit_Event()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "offboarded@test.com", "hash", "Off", "Boarded", Now));
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        await handler.HandleAsync(
            new OffboardingPlanCompletedIntegrationEvent(companyId, employeeId, planId, Now),
            CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        var reloaded = await db2.Users.FirstAsync(u => u.Id == employeeId);
        Assert.False(reloaded.IsActive);

        Assert.Single(auditPublisher.PublishedEvents, e => e is UserAutoDisabledOnOffboardingAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Is_NoOp_When_No_Linked_User()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        await handler.HandleAsync(
            new OffboardingPlanCompletedIntegrationEvent(companyId, employeeId, planId, Now),
            CancellationToken.None);

        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Is_NoOp_When_User_Already_Inactive()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            var user = ApplicationUser.Create(employeeId, "already-off@test.com", "hash", "Already", "Off", Now);
            user.Deactivate(Now);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        await handler.HandleAsync(
            new OffboardingPlanCompletedIntegrationEvent(companyId, employeeId, planId, Now),
            CancellationToken.None);

        Assert.Empty(auditPublisher.PublishedEvents);
    }
}
