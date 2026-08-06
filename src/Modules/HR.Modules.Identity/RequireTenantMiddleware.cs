using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity;

/// <summary>
/// Rejects any authenticated request that has no resolved tenant context.
/// Must be registered after UseAuthentication and UseIdentityModule so that
/// the current user (and therefore the tenant) has already been resolved.
/// </summary>
internal sealed class RequireTenantMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Endpoints marked [AllowAnonymous]/.AllowAnonymous() (e.g. HR.Api's /api/dev/* endpoints)
        // are meant to work regardless of session state — including when the caller happens to be
        // presenting a stale/unrelated Bearer token that authenticates successfully against Supabase
        // but doesn't resolve to a tenant (e.g. a leftover token from an earlier test session). Skip
        // the tenant check entirely for those; only endpoints that actually require authorization
        // should ever be blocked here.
        var allowsAnonymous = context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;

        if (!allowsAnonymous && context.User.Identity?.IsAuthenticated == true)
        {
            var resolved = context.Items[SupabaseCurrentUserResolutionMiddleware.CurrentUserItemKey]
                as ResolvedCurrentUser;

            if (resolved?.TenantId is null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "tenant_unresolved",
                    message = "The request was made by an authenticated user whose tenant could not be resolved. " +
                              "Ensure the token contains a valid company_id claim."
                });
                return;
            }
        }

        await next(context);
    }
}
