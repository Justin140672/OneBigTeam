using FastEndpoints;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.SendInvite;

internal sealed class Endpoint(
    IdentityDbContext db,
    ICurrentUser currentUser,
    IAuthorizationService authorizationService,
    IClock clock) : Endpoint<SendInviteRequest, SendInviteResponse>
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

        await SendAsync(new SendInviteResponse(invite.Token, invite.ExpiresAt), cancellation: ct);
    }
}

internal sealed record SendInviteRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public string Email { get; init; } = string.Empty;
}

internal sealed record SendInviteResponse(string Token, DateTimeOffset ExpiresAt);
