using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace HR.Modules.Identity;

/// <summary>
/// Validates that for any authenticated request targeting a route that contains a
/// {companyId} segment, the route value matches the authenticated user's company_id
/// claim. This prevents cross-tenant access via URL manipulation: a user whose token
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
        if (context.User.Identity?.IsAuthenticated == true
            && context.Request.RouteValues.TryGetValue("companyId", out var routeCompanyIdRaw)
            && Guid.TryParse(routeCompanyIdRaw?.ToString(), out var routeCompanyId))
        {
            var companyClaim = context.User.FindFirstValue("company_id");

            if (!Guid.TryParse(companyClaim, out var userCompanyId) || routeCompanyId != userCompanyId)
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
