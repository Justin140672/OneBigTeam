using HR.Modules.Identity.Features.CreatePlatformAdministrator;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.EnablePlatformAdministrator;

internal sealed class EnablePlatformAdministratorHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<EnablePlatformAdministratorResponse>> HandleAsync(
        EnablePlatformAdministratorRequest request,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!await CreatePlatformAdministratorHandler.IsEnabledPlatformOwnerAsync(db, currentUser, cancellationToken))
            return Result.Failure<EnablePlatformAdministratorResponse>(
                Error.Unauthorized("Only an enabled platform owner may manage administrator accounts."));

        var administrator = await db.PlatformAdministrators.FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (administrator is null)
            return Result.Failure<EnablePlatformAdministratorResponse>(Error.NotFound("Platform administrator was not found."));

        if (administrator.IsEnabled)
            return Result.Failure<EnablePlatformAdministratorResponse>(Error.Conflict("Platform administrator account is already enabled."));

        var now = clock.UtcNow;
        administrator.Enable(now);
        await db.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new PlatformAdministratorEnabledAuditEvent(administrator.Id, administrator.Email, currentUser.UserId, now),
            cancellationToken);

        return Result.Success(new EnablePlatformAdministratorResponse(administrator.Id, administrator.IsEnabled));
    }
}
