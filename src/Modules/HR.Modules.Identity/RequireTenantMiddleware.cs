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
        if (context.User.Identity?.IsAuthenticated == true)
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
