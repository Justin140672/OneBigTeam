using FastEndpoints;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.GetPlatformAdminMe;

internal sealed class Endpoint(
    ICurrentUser currentUser,
    IdentityDbContext dbContext) : EndpointWithoutRequest<GetPlatformAdminMeResponse>
{
    public override void Configure()
    {
        Get("/api/platform-admin/me");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var normalizedEmail = currentUser.Email?.Trim().ToLowerInvariant();

        var admin = await dbContext.PlatformAdministrators
            .Where(a => a.IsEnabled &&
                ((a.SupabaseAuthUserId == userId) ||
                 (normalizedEmail != null && a.Email == normalizedEmail)))
            .FirstOrDefaultAsync(ct);

        if (admin is null)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        // First authenticated request for an administrator whose row was created without a linked
        // identity-provider account (Admin Portal creation, or config bootstrap): back-link it to
        // the "sub" on the token now, so operations that need it (e.g. MFA reset) work without a
        // separate email lookup. Mirrors UserProfile's self-heal.
        if (admin.SupabaseAuthUserId != userId.Value)
        {
            admin.LinkSupabaseAuthUserId(userId.Value);
            await dbContext.SaveChangesAsync(ct);
        }

        await Send.ResultAsync(TypedResults.Ok(new GetPlatformAdminMeResponse(
            admin.SupabaseAuthUserId ?? userId.Value,
            admin.Email,
            admin.Role)));
    }
}

internal sealed record GetPlatformAdminMeResponse(
    Guid UserId,
    string Email,
    PlatformAdministratorRole Role);
