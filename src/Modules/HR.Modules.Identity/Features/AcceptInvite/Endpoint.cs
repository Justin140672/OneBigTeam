using FastEndpoints;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.AcceptInvite;

// Creates a real Supabase-backed UserProfile (not a local-auth ApplicationUser — that Phase A
// stand-in has been superseded here the same way SignUp's was, see SignUpHandler's remarks) so an
// invited employee can actually use every Supabase-based flow: real password-grant Login,
// RequestPasswordReset/forgot-password (which only ever looks at UserProfiles — an
// ApplicationUser-only account was silently invisible to it), etc. Uses
// ISupabaseAuthGateway.CreateConfirmedUserAsync rather than CreateUserAsync: the invite link
// itself, emailed to invite.Email when the invite was sent, already proves ownership of that
// address, so there's no separate "verify your email" step needed here the way self-service
// SignUp needs one.
internal sealed class Endpoint(
    IdentityDbContext db,
    ISupabaseAuthGateway supabaseAuthGateway,
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

        // Use the employee ID as the user ID — single identity across modules (UserRole.UserId
        // below relies on this matching, same as SignUpHandler's admin profile).
        var profileExists = await db.UserProfiles.AnyAsync(p => p.Id == invite.EmployeeId, ct);
        if (!profileExists)
        {
            Guid supabaseUserId;
            try
            {
                supabaseUserId = await supabaseAuthGateway.CreateConfirmedUserAsync(invite.Email, req.Password, ct);
            }
            catch (EmailAlreadyRegisteredException)
            {
                await Send.ResultAsync(TypedResults.Conflict(
                    new { error = "An account with this email already exists." }));
                return;
            }

            var profile = UserProfile.Create(
                invite.EmployeeId, supabaseUserId, invite.CompanyId, invite.Email,
                firstName: string.Empty, lastName: string.Empty, now);
            db.UserProfiles.Add(profile);
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
}

internal sealed record AcceptInviteRequest(string Token, string Password);

internal sealed record AcceptInviteResponse(Guid UserId);
