using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.CancelInvite;

internal sealed class CancelInviteHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<CancelInviteResponse>> HandleAsync(
        CancelInviteRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var invite = await db.UserInvites
            .FirstOrDefaultAsync(i => i.Id == request.InviteId && i.CompanyId == request.CompanyId, cancellationToken);

        if (invite is null)
            return Result.Failure<CancelInviteResponse>(Error.NotFound("Invitation was not found."));

        if (invite.IsClaimed || invite.IsCancelled)
            return Result.Failure<CancelInviteResponse>(
                Error.Conflict("Only a pending, non-cancelled invitation can be cancelled."));

        var now = clock.UtcNow;
        invite.Cancel(now);
        await db.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new UserInviteCancelledAuditEvent(invite.CompanyId, invite.EmployeeId, invite.Id, invite.Email, actorUserId, now),
            cancellationToken);

        return Result.Success(new CancelInviteResponse(invite.Id));
    }
}
