using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services.AccountEmailPolicy;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.InviteEmployeeUser;

internal sealed class InviteEmployeeUserHandler(
    IdentityDbContext db,
    IClock clock,
    IEmployeeNameReader employeeNameReader,
    IInvitationEmailSender invitationEmailSender,
    IInviteLinkBuilder inviteLinkBuilder,
    IAuditEventPublisher auditEventPublisher,
    AccountCreationEmailGuard accountCreationEmailGuard)
{
    public async Task<Result<InviteEmployeeUserResponse>> HandleAsync(
        InviteEmployeeUserRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, InviteEmployeeUserResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<InviteEmployeeUserResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, [request.EmployeeId], cancellationToken);
        if (!names.ContainsKey(request.EmployeeId))
            return Result.Failure<InviteEmployeeUserResponse>(
                Error.NotFound("Employee was not found in this company."));

        // Ticket 9: an invitation creates a login account on acceptance, so the invited address must
        // be an organisation email — rejected here, before any invite row is persisted or any
        // email is sent.
        var emailPolicy = await accountCreationEmailGuard.EnsureAllowedAsync(
            request.Email, AccountCreationPath.EmployeeInvitation,
            request.CompanyId, request.EmployeeId, actorUserId, cancellationToken);
        if (emailPolicy.IsFailure)
            return Result.Failure<InviteEmployeeUserResponse>(emailPolicy.Error);

        var hasLinkedUser = await db.Users.AnyAsync(u => u.Id == request.EmployeeId, cancellationToken)
            || await db.UserProfiles.AnyAsync(p => p.Id == request.EmployeeId, cancellationToken);
        if (hasLinkedUser)
            return Result.Failure<InviteEmployeeUserResponse>(
                Error.Conflict("This employee already has a linked user account."));

        var now = clock.UtcNow;

        var existing = await db.UserInvites
            .Where(i => i.EmployeeId == request.EmployeeId && i.ClaimedAt == null)
            .ToListAsync(cancellationToken);

        if (existing.Any(i => i.CancelledAt == null && !i.IsExpired))
            return Result.Failure<InviteEmployeeUserResponse>(
                Error.Conflict("This employee already has a pending invitation. Resend or cancel it instead."));

        db.UserInvites.RemoveRange(existing);

        var invite = UserInvite.Create(request.EmployeeId, request.CompanyId, request.Email, now, request.RoleIds, actorUserId);
        db.UserInvites.Add(invite);

        // Note: the invite link/email is sent AFTER the save, using in-memory values, so the
        // idempotency check above (which short-circuits before this point on replay) also prevents
        // a retried/duplicated request from sending a second invitation email.
        if (request.IdempotencyKey is { } key)
        {
            var precomputedResponse = new InviteEmployeeUserResponse(invite.Id, invite.EmployeeId, invite.Email, invite.ExpiresAt, EmailSent: false);
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, InviteEmployeeUserResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, precomputedResponse, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var inviteLink = inviteLinkBuilder.Build(invite.Token);
        var recipientName = names.TryGetValue(request.EmployeeId, out var n) ? n : null;

        var emailSent = await invitationEmailSender.SendAsync(
            toEmail: request.Email,
            recipientName: recipientName,
            actionUrl: inviteLink,
            ct: cancellationToken);

        if (emailSent)
        {
            invite.MarkEmailSent(now);
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new UserInvitedAuditEvent(
                request.CompanyId,
                request.EmployeeId,
                invite.Id,
                request.Email,
                request.RoleIds,
                actorUserId,
                now),
            cancellationToken);

        return Result.Success(new InviteEmployeeUserResponse(invite.Id, invite.EmployeeId, invite.Email, invite.ExpiresAt, emailSent));
    }
}
