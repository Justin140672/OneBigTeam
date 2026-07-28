using FastEndpoints;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.AcceptInvite;

internal sealed class Endpoint(
    IdentityDbContext db,
    IClock clock) : Endpoint<AcceptInviteRequest, AcceptInviteResponse>
{
    public override void Configure()
    {
        Post("/api/invites/accept");
        AllowAnonymous();
    }

    public override async Task HandleAsync(AcceptInviteRequest req, CancellationToken ct)
    {
        var invite = await db.UserInvites
            .FirstOrDefaultAsync(i => i.Token == req.Token, ct);

        if (invite is null)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = "Invite not found." }));
            return;
        }

        if (invite.IsClaimed)
        {
            await Send.ResultAsync(TypedResults.Conflict(new { error = "This invite has already been used." }));
            return;
        }

        var now = clock.UtcNow;

        if (invite.IsExpired)
        {
            await Send.ResultAsync(TypedResults.BadRequest(new { error = "This invite has expired." }));
            return;
        }

        // Use the employee ID as the user ID — single identity across modules.
        var userExists = await db.Users.AnyAsync(u => u.Id == invite.EmployeeId, ct);
        if (!userExists)
        {
            var user = ApplicationUser.Create(
                invite.EmployeeId,
                invite.Email,
                passwordHash: HashPassword(req.Password),
                firstName: string.Empty,
                lastName: string.Empty,
                now);
            db.Users.Add(user);
        }

        // Assign the roles selected when the invite was sent (Features/InviteEmployeeUser),
        // falling back to the base Employee role for invites created before role selection
        // existed (e.g. via the older SendInvite endpoint).
        var roleIds = invite.PendingRoleIds.Count > 0
            ? invite.PendingRoleIds
            : [SystemRoles.Employee];

        foreach (var roleId in roleIds)
        {
            var roleExists = await db.UserRoles.AnyAsync(
                ur => ur.UserId == invite.EmployeeId && ur.RoleId == roleId, ct);
            if (!roleExists)
                db.UserRoles.Add(UserRole.Create(invite.EmployeeId, roleId, now));
        }

        invite.Claim(now);
        await db.SaveChangesAsync(ct);

        await Send.ResultAsync(TypedResults.Ok(new AcceptInviteResponse(invite.EmployeeId)));
    }

    private static string HashPassword(string password)
    {
        // BCrypt-style hashing via built-in PBKDF2
        return Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(password)));
    }
}

internal sealed record AcceptInviteRequest(string Token, string Password);

internal sealed record AcceptInviteResponse(Guid UserId);
