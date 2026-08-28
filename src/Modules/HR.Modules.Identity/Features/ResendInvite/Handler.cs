using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.ResendInvite;

internal sealed class ResendInviteHandler(
    IdentityDbContext db,
    IClock clock,
    IInvitationEmailSender invitationEmailSender,
    IInviteLinkBuilder inviteLinkBuilder,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<ResendInviteResponse>> HandleAsync(
        ResendInviteRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var invite = await db.UserInvites
            .FirstOrDefaultAsync(i => i.Id == request.InviteId && i.CompanyId == request.CompanyId, cancellationToken);

        if (invite is null)
            return Result.Failure<ResendInviteResponse>(Error.NotFound("Invitation was not found."));

        if (invite.IsClaimed || invite.IsCancelled)
            return Result.Failure<ResendInviteResponse>(
                Error.Conflict("Only a pending, non-cancelled invitation can be resent."));

        var now = clock.UtcNow;
        invite.Resend(now);
        await db.SaveChangesAsync(cancellationToken);

        var inviteLink = inviteLinkBuilder.Build(invite.Token);

        var emailSent = await invitationEmailSender.SendAsync(
            toEmail: invite.Email,
            recipientName: null,
            actionUrl: inviteLink,
            ct: cancellationToken);

        await auditEventPublisher.PublishAsync(
            new UserInviteResentAuditEvent(invite.CompanyId, invite.EmployeeId, invite.Id, invite.Email, actorUserId, now),
            cancellationToken);

        return Result.Success(new ResendInviteResponse(invite.Id, invite.ExpiresAt, emailSent));
    }
}
