using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.ResendInvite;

internal sealed class ResendInviteHandler(
    IdentityDbContext db,
    IClock clock,
    IEmailSender emailSender,
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

        try
        {
            await emailSender.SendAsync(
                toEmail: invite.Email,
                subject: "You have been invited to One Big Team",
                htmlBody: $"""
                    <html><body style="font-family:sans-serif;max-width:600px;margin:auto;padding:24px">
                    <h1>Welcome to One Big Team</h1>
                    <p>This is a reminder that you've been invited to join the platform.</p>
                    <p style="margin:24px 0"><a href="{inviteLink}" style="background:#0d6efd;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">Accept Invitation</a></p>
                    <p style="word-break:break-all">{inviteLink}</p>
                    <p style="color:#666;font-size:0.85em">This invitation expires on {invite.ExpiresAt:f} UTC.</p>
                    </body></html>
                    """,
                ct: cancellationToken);
        }
        catch
        {
            // Email failure must not prevent the token/expiry regeneration from being saved.
        }

        await auditEventPublisher.PublishAsync(
            new UserInviteResentAuditEvent(invite.CompanyId, invite.EmployeeId, invite.Id, invite.Email, actorUserId, now),
            cancellationToken);

        return Result.Success(new ResendInviteResponse(invite.Id, invite.ExpiresAt));
    }
}
