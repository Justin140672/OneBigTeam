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
    IEmailSender emailSender,
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

        // No existing linked user — ApplicationUser.Id == EmployeeId by convention (see UserInvite.EmployeeId).
        var hasLinkedUser = await db.Users.AnyAsync(u => u.Id == request.EmployeeId, cancellationToken);
        if (hasLinkedUser)
            return Result.Failure<InviteEmployeeUserResponse>(
                Error.Conflict("This employee already has a linked user account."));

        var now = clock.UtcNow;

        // Superseding any existing pending invites for this employee, same as SendInvite.
        var existing = await db.UserInvites
            .Where(i => i.EmployeeId == request.EmployeeId && i.ClaimedAt == null)
            .ToListAsync(cancellationToken);
        db.UserInvites.RemoveRange(existing);

        var invite = UserInvite.Create(request.EmployeeId, request.CompanyId, request.Email, now, request.RoleIds, actorUserId);
        db.UserInvites.Add(invite);
        await db.SaveChangesAsync(cancellationToken);

        var inviteLink = inviteLinkBuilder.Build(invite.Token);

        try
        {
            await emailSender.SendAsync(
                toEmail: request.Email,
                subject: "You have been invited to One Big Team",
                htmlBody: BuildEmailHtml(inviteLink, invite.ExpiresAt),
                ct: cancellationToken);
        }
        catch
        {
            // Email failure must not prevent the invite from being saved — mirrors SendInvite.
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

        return Result.Success(new InviteEmployeeUserResponse(invite.Id, invite.EmployeeId, invite.Email, invite.ExpiresAt));
    }

    private static string BuildEmailHtml(string inviteLink, DateTimeOffset expiresAt) => $"""
        <html>
        <body style="font-family:sans-serif;max-width:600px;margin:auto;padding:24px">
          <h1>Welcome to One Big Team</h1>
          <p>You have been invited to join the platform. Click the link below to activate your account and set a password.</p>
          <p style="margin:24px 0">
            <a href="{inviteLink}" style="background:#0d6efd;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">
              Accept Invitation
            </a>
          </p>
          <p>Or copy this link into your browser:</p>
          <p style="word-break:break-all">{inviteLink}</p>
          <hr/>
          <p style="color:#666;font-size:0.85em">This invitation expires on {expiresAt:f} UTC.</p>
        </body>
        </html>
        """;
}
