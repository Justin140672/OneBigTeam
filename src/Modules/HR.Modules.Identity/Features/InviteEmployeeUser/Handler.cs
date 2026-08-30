using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.InviteEmployeeUser;

internal sealed class InviteEmployeeUserHandler(
    IdentityDbContext db,
    IClock clock,
    IEmployeeNameReader employeeNameReader,
    IInvitationEmailSender invitationEmailSender,
    IInviteLinkBuilder inviteLinkBuilder,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<InviteEmployeeUserResponse>> HandleAsync(
        InviteEmployeeUserRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        // Employee must belong to this company (per the shared name-reader port — employees not
        // found in the company simply won't appear in the returned dictionary).
        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, [request.EmployeeId], cancellationToken);
        if (!names.ContainsKey(request.EmployeeId))
            return Result.Failure<InviteEmployeeUserResponse>(
                Error.NotFound("Employee was not found in this company."));

        // No existing linked account — ApplicationUser.Id == EmployeeId by convention (see
        // UserInvite.EmployeeId). Real Supabase-backed accounts (self-service SignUp, AcceptInvite)
        // live in UserProfiles rather than Users, so both must be checked (ADM-01).
        var hasLinkedUser = await db.Users.AnyAsync(u => u.Id == request.EmployeeId, cancellationToken)
            || await db.UserProfiles.AnyAsync(p => p.Id == request.EmployeeId, cancellationToken);
        if (hasLinkedUser)
            return Result.Failure<InviteEmployeeUserResponse>(
                Error.Conflict("This employee already has a linked user account."));

        var now = clock.UtcNow;

        // ADM-01: an actionable (still-pending, not expired) invitation must be resent or cancelled
        // via its own actions rather than silently replaced from here. An expired or cancelled
        // invite is not actionable, so it can be superseded by a fresh one.
        var existing = await db.UserInvites
            .Where(i => i.EmployeeId == request.EmployeeId && i.ClaimedAt == null)
            .ToListAsync(cancellationToken);

        if (existing.Any(i => i.CancelledAt == null && !i.IsExpired))
            return Result.Failure<InviteEmployeeUserResponse>(
                Error.Conflict("This employee already has a pending invitation. Resend or cancel it instead."));

        db.UserInvites.RemoveRange(existing);

        var invite = UserInvite.Create(request.EmployeeId, request.CompanyId, request.Email, now, request.RoleIds, actorUserId);
        db.UserInvites.Add(invite);
        await db.SaveChangesAsync(cancellationToken);

        var inviteLink = inviteLinkBuilder.Build(invite.Token);
        var recipientName = names.TryGetValue(request.EmployeeId, out var n) ? n : null;

        var emailSent = await invitationEmailSender.SendAsync(
            toEmail: request.Email,
            recipientName: recipientName,
            actionUrl: inviteLink,
            ct: cancellationToken);

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
