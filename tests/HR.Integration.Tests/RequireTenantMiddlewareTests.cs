using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity;
using Microsoft.AspNetCore.Http;

namespace HR.Integration.Tests;

public class RequireTenantMiddlewareTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public RequireTenantMiddlewareTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Authenticated_Request_Without_Tenant_Returns_403()
    {
        using var client = _factory.CreateClient();
        // Authenticated (X-Test-User present) but no X-Test-Tenant header
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-without-tenant");

        var response = await client.PostAsJsonAsync("/api/companies", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_Request_Is_Not_Blocked_By_Tenant_Guard()
    {
        using var client = _factory.CreateClient();
        // No auth headers at all — should get 401 from auth, not 403 from tenant guard

        var response = await client.PostAsJsonAsync("/api/companies", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_Request_With_Tenant_Passes_Guard()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "user-with-tenant");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, "tenant-abc");

        // We only care that the tenant guard passes (not 403). The actual endpoint
        // response (200, 404, etc.) is tested in the endpoint-specific tests.
        var response = await client.PostAsJsonAsync("/api/companies", new { });

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

public class RequireTenantMiddlewareUnitTests
{
    [Fact]
    public async Task Middleware_Passes_Through_Unauthenticated_Request()
    {
        var context = new DefaultHttpContext();
        // No authentication — IsAuthenticated defaults to false

        var nextCalled = false;
        var middleware = new RequireTenantMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Middleware_Returns_403_When_Authenticated_But_No_Tenant()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([], authenticationType: "test"));
        // No ResolvedCurrentUser in Items → tenant is null

        var nextCalled = false;
        var middleware = new RequireTenantMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_Passes_Through_When_Tenant_Resolved()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([], authenticationType: "test"));
        context.Items[SupabaseCurrentUserResolutionMiddleware.CurrentUserItemKey] =
            new ResolvedCurrentUser(Guid.NewGuid(), "user@acme.com", "tenant-xyz", IsAuthenticated: true);

        var nextCalled = false;
        var middleware = new RequireTenantMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }
}
