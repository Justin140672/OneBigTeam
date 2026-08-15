using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity;

/// <summary>
/// Validates that for any authenticated request targeting a route that contains a
/// {companyId} segment, the route value matches the authenticated user's resolved tenant.
/// This prevents cross-tenant access via URL manipulation: a user whose token
/// belongs to Acme cannot reach Beta Corp resources by substituting BetaCorpId in
/// the URL.
///
/// RequireTenantMiddleware proves the user *belongs to a tenant*; this middleware
/// proves they are accessing *their* tenant's routes.
/// </summary>
internal sealed class TenantRouteAuthorizationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // "platform:admin"-policy endpoints (Admin Portal: Customer Details, Billing
        // Breakdown/History, Support Sessions, Deletion/Trial/Read-Only management, etc.) are
        // deliberately company-agnostic — see RequireTenantMiddleware's identical exemption and
        // IdentityModule.AddRolePolicies's remarks. Several of these routes contain a
        // {companyId} segment (identifying the *customer being administered*, not the caller's
        // own tenant), so without this exemption this middleware 403s every such request
        // unconditionally — a platform admin has no resolved tenant of their own to match against.
        var isPlatformAdminPolicy = context.GetEndpoint()?.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Any(a => a.Policy == "platform:admin") == true;

        if (!isPlatformAdminPolicy
            && context.User.Identity?.IsAuthenticated == true
            && context.Request.RouteValues.TryGetValue("companyId", out var routeCompanyIdRaw)
            && Guid.TryParse(routeCompanyIdRaw?.ToString(), out var routeCompanyId))
        {
            // Read the DB-resolved tenant (SupabaseCurrentUserResolutionMiddleware), not a raw
            // "company_id" JWT claim — real Supabase-issued tokens never carry one (Supabase has no
            // concept of this app's tenants), so relying on the claim directly would 403 every
            // company-scoped route unconditionally for every real Supabase-authenticated user,
            // regardless of their correctly resolved tenant.
            var resolved = context.Items[SupabaseCurrentUserResolutionMiddleware.CurrentUserItemKey]
                as ResolvedCurrentUser;

            if (!Guid.TryParse(resolved?.TenantId, out var userCompanyId) || routeCompanyId != userCompanyId)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error   = "forbidden",
                    message = "Access to the requested company is not permitted."
                });
                return;
            }
        }

        await next(context);
    }
}
