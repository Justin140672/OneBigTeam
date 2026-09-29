using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity;

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
