using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.DisableUser;

// Manual counterpart to the automatic disable wired up in Features/OnOffboardingPlanCompleted.
// ApplicationUser.Deactivate() previously existed in the domain but had no caller anywhere in the
// codebase — this endpoint is the first place it's actually invoked for a manual admin action.
// Disabling only flips IsActive; no related data (roles, invites, historical records) is touched
// or cascade-deleted.
internal sealed class DisableUserHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ITargetUserCompanyGuard targetUserCompanyGuard)
{
    public async Task<Result<DisableUserResponse>> HandleAsync(
        DisableUserRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        // IAM-01: confirm the target user belongs to the route company before touching account status.
        var isMember = await targetUserCompanyGuard.IsMemberAsync(request.CompanyId, request.UserId, cancellationToken);
        if (!isMember)
            return Result.Failure<DisableUserResponse>(Error.NotFound("User was not found."));

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null)
            return Result.Failure<DisableUserResponse>(Error.NotFound("User was not found."));

        if (!user.IsActive)
            return Result.Failure<DisableUserResponse>(Error.Conflict("User account is already disabled."));

        var now = clock.UtcNow;
        user.Deactivate(now);
        await db.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new UserDisabledAuditEvent(request.CompanyId, user.Id, user.Id, actorUserId, now),
            cancellationToken);

        return Result.Success(new DisableUserResponse(user.Id, user.IsActive));
    }
}
