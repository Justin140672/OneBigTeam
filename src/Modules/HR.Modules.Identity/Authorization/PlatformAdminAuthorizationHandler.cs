using HR.Modules.Identity.Persistence;
using HR.SharedKernel;

using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Authorization;

/// <summary>
/// Real, DB-backed enforcement point for the "platform:admin" policy (SEC-002 fix). Succeeds only
/// when the caller matches an enabled row in identity.platform_administrators — matched first by
/// SupabaseAuthUserId (ICurrentUser.UserId is the Supabase "sub" claim — see
/// HttpContextCurrentUser.UserId), falling back to a case-insensitive email match for
/// administrators not yet linked to a Supabase auth user (mirrors PlatformAdministrator's
/// nullable SupabaseAuthUserId design intent).
///
/// This replaces the previous "any authenticated user" policy, which was a privilege-escalation
/// hole: GetPlatformSettings/UpdatePlatformSettings had no handler-level allow-list check (unlike
/// ~23 other Companies-module handlers that separately check PlatformAdmin:AllowedEmails config),
/// so any authenticated user of any tenant could read/mutate global platform settings.
///
/// The existing handler-by-handler config allow-list checks elsewhere are intentionally left in
/// place for now (defense-in-depth) — removing them is a later step once this becomes the sole
/// enforcement point everywhere.
/// </summary>
internal sealed class PlatformAdminAuthorizationHandler(
    ICurrentUser currentUser,
    IdentityDbContext dbContext) : AuthorizationHandler<PlatformAdminRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PlatformAdminRequirement requirement)
    {
        var userId = currentUser.UserId;
        var email = currentUser.Email;

        if (userId is null && string.IsNullOrWhiteSpace(email))
            return;

        var normalizedEmail = email?.Trim().ToLowerInvariant();

        var isPlatformAdmin = await dbContext.PlatformAdministrators
            .AsNoTracking()
            .AnyAsync(a => a.IsEnabled &&
                ((userId != null && a.SupabaseAuthUserId == userId) ||
                 (normalizedEmail != null && a.Email == normalizedEmail)));

        if (isPlatformAdmin)
            context.Succeed(requirement);
    }
}
