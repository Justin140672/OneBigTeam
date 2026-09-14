using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
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
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, ResendInviteResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<ResendInviteResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var invite = await db.UserInvites
            .FirstOrDefaultAsync(i => i.Id == request.InviteId && i.CompanyId == request.CompanyId, cancellationToken);

        if (invite is null)
            return Result.Failure<ResendInviteResponse>(Error.NotFound("Invitation was not found."));

        if (invite.IsClaimed || invite.IsCancelled)
            return Result.Failure<ResendInviteResponse>(
                Error.Conflict("Only a pending, non-cancelled invitation can be resent."));

        var now = clock.UtcNow;
        invite.Resend(now);

        if (request.IdempotencyKey is { } key)
        {
            var precomputedResponse = new ResendInviteResponse(invite.Id, invite.ExpiresAt, EmailSent: false);
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, ResendInviteResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, precomputedResponse, new DateTimeOffset(now, TimeSpan.Zero), cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

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
