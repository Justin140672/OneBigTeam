using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;

namespace HR.Modules.Identity.Features.OnPositionProfileUpserted;

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
