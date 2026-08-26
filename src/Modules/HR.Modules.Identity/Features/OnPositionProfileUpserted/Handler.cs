using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;

namespace HR.Modules.Identity.Features.OnPositionProfileUpserted;

// IAM-03: eager half of Position sync — HR.Modules.Employees publishes this whenever a
// PositionProfile is created/updated/deactivated (see PositionProfileUpsertedIntegrationEvent's
// remarks), so an administrator can see and configure default roles for a brand-new position
// immediately, without waiting for the first employee assignment. PositionSync.EnsureExistsAsync
// (the lazy half, used by Features/SetPositionRoleDefaults and the employee-assignment consumers)
// covers the same ground for any position not yet synced this way, so this handler failing/missing
// an event (e.g. a dropped delivery) is self-healing rather than a hard dependency.
internal sealed class Handler(
    IdentityDbContext db,
    HR.Modules.Identity.Services.PositionSync positionSync)
    : IIntegrationEventHandler<PositionProfileUpsertedIntegrationEvent>
{
    public async Task HandleAsync(PositionProfileUpsertedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await positionSync.EnsureExistsAsync(
            integrationEvent.CompanyId, integrationEvent.PositionProfileId, integrationEvent.OccurredAt, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
