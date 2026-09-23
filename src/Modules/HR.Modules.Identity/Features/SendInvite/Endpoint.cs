using FastEndpoints;
using Microsoft.AspNetCore.Http;

using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services.AccountEmailPolicy;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Features.SendInvite;

internal sealed class Endpoint(
    IdentityDbContext db,
    IClock clock,
    IInvitationEmailSender invitationEmailSender,
    IInviteLinkBuilder inviteLinkBuilder,
    AccountCreationEmailGuard accountCreationEmailGuard,
    ICurrentUser currentUser,
    ILogger<Endpoint> logger) : Endpoint<SendInviteRequest, SendInviteResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/invite");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(SendInviteRequest req, CancellationToken ct)
    {
        // Ticket 9: same work-email rule as InviteEmployeeUser — checked before any existing invite
        // is replaced, any new invite is persisted, or any email is sent.
        var emailPolicy = await accountCreationEmailGuard.EnsureAllowedAsync(
            req.Email, AccountCreationPath.EmployeeInvitation,
            req.CompanyId, req.EmployeeId, currentUser.UserId, ct);
        if (emailPolicy.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(emailPolicy.Error));
            return;
        }

        var now = clock.UtcNow;

        // Cancel any existing pending invites for this employee
        var existing = await db.UserInvites
            .Where(i => i.EmployeeId == req.EmployeeId && i.ClaimedAt == null)
            .ToListAsync(ct);
        db.UserInvites.RemoveRange(existing);

        var invite = UserInvite.Create(req.EmployeeId, req.CompanyId, req.Email, now);
        db.UserInvites.Add(invite);
        await db.SaveChangesAsync(ct);

        var inviteLink = inviteLinkBuilder.Build(invite.Token);

        var emailSent = await invitationEmailSender.SendAsync(
            toEmail: req.Email,
            recipientName: null,
            actionUrl: inviteLink,
            ct: ct);

        if (!emailSent)
        {
            logger.LogWarning(
                "Invitation email could not be sent. EmployeeId={EmployeeId} To={Email}",
                req.EmployeeId,
                req.Email);
        }

        await Send.ResultAsync(TypedResults.Ok(new SendInviteResponse(invite.Token, invite.ExpiresAt, emailSent)));
    }
}

internal sealed record SendInviteRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public string Email { get; init; } = string.Empty;
}

internal sealed record SendInviteResponse(string Token, DateTimeOffset ExpiresAt, bool EmailSent);
