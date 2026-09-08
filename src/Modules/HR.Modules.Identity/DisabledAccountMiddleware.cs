using HR.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity;

/// <summary>
/// Rejects any authenticated request whose linked application account has been disabled — whether
/// disabled manually (Features/DisableUser) or automatically when an offboarding plan completes
/// (Features/OnOffboardingPlanCompleted). Both paths flip the single <c>ApplicationUser.IsActive</c>
/// flag, so checking that flag on every request is enough to cover both.
///
/// This closes the gap where an already-issued, still-valid Supabase access token kept working
/// until it expired even after the account was disabled: Supabase Auth itself is never told about
/// the disablement, so token validation alone can never catch it.
///
/// Must run after <see cref="SupabaseCurrentUserResolutionMiddleware"/> (which resolves the
/// application user id) and before authorization.
///
/// Platform administrators are exempted ONLY on genuine platform-administration endpoints (those
/// guarded by the "platform:admin" policy — detected via endpoint <see cref="IAuthorizeData"/>
/// metadata, which FastEndpoints' <c>Policies("platform:admin")</c> emits). On every ordinary
/// company endpoint a disabled <c>ApplicationUser</c> is still rejected even when the same person is
/// also an enabled platform administrator — otherwise the disabled company account's retained
/// permissions would stay usable. A platform administrator with no <c>ApplicationUser</c> row (or an
/// active one) is unaffected either way.
/// </summary>
internal sealed class DisabledAccountMiddleware(RequestDelegate next)
{
    private const string PlatformAdminPolicy = "platform:admin";

    public async Task InvokeAsync(HttpContext context, IdentityDbContext dbContext)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        // Anonymous endpoints (login, logout, dev helpers, health) must keep working regardless of
        // account state — mirrors the same exemption in RequireTenantMiddleware.
        var allowsAnonymous = context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
        if (allowsAnonymous)
        {
            await next(context);
            return;
        }

        var resolved = context.Items[SupabaseCurrentUserResolutionMiddleware.CurrentUserItemKey]
            as ResolvedCurrentUser;

        var resolvedUserId = resolved?.UserId;
        var normalizedEmail = resolved?.Email?.Trim().ToLowerInvariant();

        if (resolvedUserId is Guid userId)
        {
            // By convention ApplicationUser.Id == UserProfile.Id == EmployeeId, and
            // SupabaseCurrentUserResolutionMiddleware sets ResolvedCurrentUser.UserId to
            // UserProfile.Id once a profile exists.
            var accountIsActive = await dbContext.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => (bool?)u.IsActive)
                .FirstOrDefaultAsync(context.RequestAborted);

            if (accountIsActive == false)
            {
                // A disabled company account stays blocked everywhere except a genuine
                // platform-administration endpoint accessed by an enabled platform administrator —
                // matched exactly as PlatformAdminAuthorizationHandler does (enabled row, by
                // SupabaseAuthUserId or email).
                if (IsPlatformAdminEndpoint(context))
                {
                    var isPlatformAdmin = await dbContext.PlatformAdministrators
                        .AsNoTracking()
                        .AnyAsync(
                            a => a.IsEnabled &&
                                 ((a.SupabaseAuthUserId == userId) ||
                                  (normalizedEmail != null && a.Email == normalizedEmail)),
                            context.RequestAborted);

                    if (isPlatformAdmin)
                    {
                        await next(context);
                        return;
                    }
                }

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "account_disabled",
                    message = "This account has been disabled. Contact your administrator for access.",
                });
                return;
            }
        }

        await next(context);
    }

    private static bool IsPlatformAdminEndpoint(HttpContext context) =>
        context.GetEndpoint()?.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Any(a => string.Equals(a.Policy, PlatformAdminPolicy, StringComparison.Ordinal)) ?? false;
}
