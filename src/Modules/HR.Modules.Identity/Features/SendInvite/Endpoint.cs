using FastEndpoints;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Features.SendInvite;

internal sealed class Endpoint(
    IdentityDbContext db,
    ICurrentUser currentUser,
    IAuthorizationService authorizationService,
    IClock clock,
    IEmailSender emailSender,
    IInviteLinkBuilder inviteLinkBuilder,
    ILogger<Endpoint> logger) : Endpoint<SendInviteRequest, SendInviteResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/invite");
        Policies("authenticated");
    }

    public override async Task HandleAsync(SendInviteRequest req, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (userId is null ||
            !await authorizationService.HasPermissionAsync(userId.Value, HR.SharedKernel.SystemPermissions.EmployeeCreate, ct))
        {
            await SendResultAsync(TypedResults.Forbid());
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

        try
        {
            await emailSender.SendAsync(
                toEmail: req.Email,
                subject: "You have been invited to One Big Team",
                htmlBody: BuildEmailHtml(inviteLink, invite.ExpiresAt),
                ct: ct);
        }
        catch (Exception ex)
        {
            // Email failure must not prevent the invite from being saved or the token being returned.
            // Log the failure and continue so the admin can still share the link manually.
            logger.LogError(ex,
                "Failed to send invite email to {Email} for EmployeeId={EmployeeId}",
                req.Email,
                req.EmployeeId);
        }

        await SendAsync(new SendInviteResponse(invite.Token, invite.ExpiresAt), cancellation: ct);
    }

    private static string BuildEmailHtml(string inviteLink, DateTimeOffset expiresAt)
    {
        return $"""
            <html>
            <body style="font-family:sans-serif;max-width:600px;margin:auto;padding:24px">
              <h1>Welcome to One Big Team</h1>
              <p>You have been invited to join the platform. Click the link below to activate your account and set a password.</p>
              <p style="margin:24px 0">
                <a href="{inviteLink}"
                   style="background:#0d6efd;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">
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
}

internal sealed record SendInviteRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public string Email { get; init; } = string.Empty;
}

internal sealed record SendInviteResponse(string Token, DateTimeOffset ExpiresAt);
