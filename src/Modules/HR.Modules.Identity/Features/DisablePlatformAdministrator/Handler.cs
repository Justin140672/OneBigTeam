using HR.Modules.Identity.Features.CreatePlatformAdministrator;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.DisablePlatformAdministrator;

internal sealed class DisablePlatformAdministratorHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<DisablePlatformAdministratorResponse>> HandleAsync(
        DisablePlatformAdministratorRequest request,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!await CreatePlatformAdministratorHandler.IsEnabledPlatformOwnerAsync(db, currentUser, cancellationToken))
            return Result.Failure<DisablePlatformAdministratorResponse>(
                Error.Unauthorized("Only an enabled platform owner may manage administrator accounts."));

        var administrator = await db.PlatformAdministrators.FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (administrator is null)
            return Result.Failure<DisablePlatformAdministratorResponse>(Error.NotFound("Platform administrator was not found."));

        if (!administrator.IsEnabled)
            return Result.Failure<DisablePlatformAdministratorResponse>(Error.Conflict("Platform administrator account is already disabled."));

        var now = clock.UtcNow;
        administrator.Disable(now, currentUser.UserId);
        await db.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new PlatformAdministratorDisabledAuditEvent(administrator.Id, administrator.Email, currentUser.UserId, now),
            cancellationToken);

        return Result.Success(new DisablePlatformAdministratorResponse(administrator.Id, administrator.IsEnabled));
    }
}
