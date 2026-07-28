using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.UpdateUserRoles;

internal sealed class UpdateUserRolesHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<UpdateUserRolesResponse>> HandleAsync(
        UpdateUserRolesRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null)
            return Result.Failure<UpdateUserRolesResponse>(Error.NotFound("User was not found."));

        var requestedRoleIds = request.RoleIds.Distinct().ToList();
        if (requestedRoleIds.Count == 0)
            return Result.Failure<UpdateUserRolesResponse>(
                Error.Validation("A user must always retain at least one role."));

        var validRoleIds = await db.Roles
            .Where(r => requestedRoleIds.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (validRoleIds.Count != requestedRoleIds.Count)
            return Result.Failure<UpdateUserRolesResponse>(Error.Validation("One or more roles do not exist."));

        var currentRoles = await db.UserRoles
            .Where(ur => ur.UserId == request.UserId)
            .ToListAsync(cancellationToken);
        var currentRoleIds = currentRoles.Select(ur => ur.RoleId).ToList();

        var now = clock.UtcNow;

        var toRemove = currentRoles.Where(ur => !requestedRoleIds.Contains(ur.RoleId)).ToList();
        db.UserRoles.RemoveRange(toRemove);

        var toAdd = requestedRoleIds.Where(id => !currentRoleIds.Contains(id)).ToList();
        foreach (var roleId in toAdd)
            db.UserRoles.Add(UserRole.Create(request.UserId, roleId, now));

        await db.SaveChangesAsync(cancellationToken);

        if (toRemove.Count > 0 || toAdd.Count > 0)
        {
            await auditEventPublisher.PublishAsync(
                new UserRolesChangedAuditEvent(
                    request.CompanyId,
                    request.UserId,
                    request.UserId, // ApplicationUser.Id == EmployeeId by convention
                    currentRoleIds,
                    requestedRoleIds,
                    actorUserId,
                    now),
                cancellationToken);
        }

        return Result.Success(new UpdateUserRolesResponse(request.UserId, requestedRoleIds));
    }
}
