using HR.Modules.Identity.Features.CreatePlatformAdministrator;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.AssignPlatformAdministratorRole;

internal sealed class AssignPlatformAdministratorRoleHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<AssignPlatformAdministratorRoleResponse>> HandleAsync(
        AssignPlatformAdministratorRoleRequest request,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!await CreatePlatformAdministratorHandler.IsEnabledPlatformOwnerAsync(db, currentUser, cancellationToken))
            return Result.Failure<AssignPlatformAdministratorRoleResponse>(
                Error.Unauthorized("Only an enabled platform owner may manage administrator accounts."));

        var administrator = await db.PlatformAdministrators.FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (administrator is null)
            return Result.Failure<AssignPlatformAdministratorRoleResponse>(Error.NotFound("Platform administrator was not found."));

        var beforeRole = administrator.Role;
        administrator.AssignRole(request.Role);
        await db.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new PlatformAdministratorRoleAssignedAuditEvent(
                administrator.Id, administrator.Email, beforeRole, administrator.Role, currentUser.UserId, clock.UtcNow),
            cancellationToken);

        return Result.Success(new AssignPlatformAdministratorRoleResponse(administrator.Id, administrator.Role));
    }
}
