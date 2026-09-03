using System.Security.Claims;
using HR.Modules.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// SEC-001 / TEST-002 unit coverage for <see cref="TenantRouteAuthorizationMiddleware"/> in
/// isolation (no HTTP pipeline, no database): a <see cref="DefaultHttpContext"/> is built by hand
/// with the route value, <c>context.Items</c> entry and endpoint metadata the middleware inspects,
/// and <c>next</c> is a fake delegate that records whether it ran.
/// </summary>
public class TenantRouteAuthorizationMiddlewareUnitTests
{
    private static (HttpContext context, Func<bool> nextCalled, TenantRouteAuthorizationMiddleware middleware)
        Build(Action<HttpContext> configure)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        configure(context);

        var called = false;
        var middleware = new TenantRouteAuthorizationMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        return (context, () => called, middleware);
    }

    private static void Authenticate(HttpContext context) =>
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString())], authenticationType: "test"));

    private static void SetResolvedTenant(HttpContext context, Guid tenantId) =>
        context.Items[SupabaseCurrentUserResolutionMiddleware.CurrentUserItemKey] =
            new ResolvedCurrentUser(
                UserId: Guid.NewGuid(),
                Email: null,
                TenantId: tenantId.ToString(),
                IsAuthenticated: true);

    private static void SetPlatformAdminEndpoint(HttpContext context) =>
        context.SetEndpoint(new Endpoint(
            requestDelegate: null,
            new EndpointMetadataCollection(new AuthorizeAttribute("platform:admin")),
            displayName: "platform-admin-endpoint"));

    [Fact]
    public async Task No_Resolved_Tenant_In_Items_Returns_403_And_Does_Not_Call_Next()
    {
        var (context, nextCalled, middleware) = Build(ctx =>
        {
            Authenticate(ctx);
            ctx.Request.RouteValues["companyId"] = Guid.NewGuid().ToString();
            // Deliberately no ResolvedCurrentUser in ctx.Items.
        });

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled());
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Unparseable_Route_CompanyId_Falls_Through_To_Next()
    {
        // The middleware only acts when the route value parses as a Guid; a non-Guid value
        // (which the ":guid" route constraint would reject in production anyway) is ignored.
        var (context, nextCalled, middleware) = Build(ctx =>
        {
            Authenticate(ctx);
            SetResolvedTenant(ctx, Guid.NewGuid());
            ctx.Request.RouteValues["companyId"] = "not-a-guid";
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
        Assert.NotEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Route_CompanyId_Matching_Resolved_Tenant_Calls_Next()
    {
        var companyId = Guid.NewGuid();
        var (context, nextCalled, middleware) = Build(ctx =>
        {
            Authenticate(ctx);
            SetResolvedTenant(ctx, companyId);
            ctx.Request.RouteValues["companyId"] = companyId.ToString();
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
    }

    [Fact]
    public async Task Route_CompanyId_Mismatching_Resolved_Tenant_Returns_403_And_Does_Not_Call_Next()
    {
        var (context, nextCalled, middleware) = Build(ctx =>
        {
            Authenticate(ctx);
            SetResolvedTenant(ctx, Guid.NewGuid());
            ctx.Request.RouteValues["companyId"] = Guid.NewGuid().ToString();
        });

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled());
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Platform_Administrator_Endpoint_Is_Exempt_Even_On_Tenant_Mismatch()
    {
        var (context, nextCalled, middleware) = Build(ctx =>
        {
            Authenticate(ctx);
            SetResolvedTenant(ctx, Guid.NewGuid());
            ctx.Request.RouteValues["companyId"] = Guid.NewGuid().ToString(); // different customer
            SetPlatformAdminEndpoint(ctx);
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
        Assert.NotEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }
}
