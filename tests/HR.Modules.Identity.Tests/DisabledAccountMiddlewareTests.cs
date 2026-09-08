using System.Security.Claims;
using System.Text.Json;
using HR.Modules.Identity;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// Ticket 1 unit coverage for <see cref="DisabledAccountMiddleware"/> in isolation: a
/// <see cref="DefaultHttpContext"/> is built by hand with the authenticated principal,
/// <c>context.Items</c> entry and endpoint metadata the middleware inspects, an in-memory
/// <see cref="IdentityDbContext"/> holds the account/platform-admin rows, and <c>next</c> is a
/// fake delegate that records whether it ran.
/// </summary>
public class DisabledAccountMiddlewareTests
{
    private static IdentityDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new IdentityDbContext(options);
    }

    private static (HttpContext context, Func<bool> nextCalled, DisabledAccountMiddleware middleware)
        Build(Action<HttpContext> configure)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection().BuildServiceProvider();
        configure(context);

        var called = false;
        var middleware = new DisabledAccountMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        return (context, () => called, middleware);
    }

    private static void Authenticate(HttpContext context, Guid? sub = null) =>
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", (sub ?? Guid.NewGuid()).ToString())], authenticationType: "test"));

    private static void SetResolved(HttpContext context, Guid? userId, string? email = null) =>
        context.Items[SupabaseCurrentUserResolutionMiddleware.CurrentUserItemKey] =
            new ResolvedCurrentUser(userId, email, TenantId: Guid.NewGuid().ToString(), IsAuthenticated: true);

    private static void SetAllowAnonymousEndpoint(HttpContext context) =>
        context.SetEndpoint(new Endpoint(
            requestDelegate: null,
            new EndpointMetadataCollection(new AllowAnonymousAttribute()),
            displayName: "anonymous-endpoint"));

    private static void SetPlatformAdminEndpoint(HttpContext context) =>
        context.SetEndpoint(new Endpoint(
            requestDelegate: null,
            new EndpointMetadataCollection(new AuthorizeAttribute("platform:admin")),
            displayName: "platform-admin-endpoint"));

    private static void SetCompanyEndpoint(HttpContext context) =>
        context.SetEndpoint(new Endpoint(
            requestDelegate: null,
            new EndpointMetadataCollection(new AuthorizeAttribute("role:hr-administrator")),
            displayName: "company-endpoint"));

    private static ApplicationUser CreateUser(Guid id, bool active)
    {
        var user = ApplicationUser.Create(id, $"{id:N}@example.com", "hash", "First", "Last", DateTimeOffset.UtcNow);
        if (!active)
            user.Deactivate(DateTimeOffset.UtcNow);
        return user;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task Authenticated_Active_Account_Calls_Next_And_Does_Not_403()
    {
        await using var db = BuildContext();
        var userId = Guid.NewGuid();
        db.Users.Add(CreateUser(userId, active: true));
        await db.SaveChangesAsync();

        var (context, nextCalled, middleware) = Build(ctx =>
        {
            Authenticate(ctx, userId);
            SetResolved(ctx, userId);
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled());
        Assert.NotEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_Disabled_Account_Returns_403_And_Does_Not_Call_Next()
    {
        await using var db = BuildContext();
        var userId = Guid.NewGuid();
        db.Users.Add(CreateUser(userId, active: false));
        await db.SaveChangesAsync();

        var (context, nextCalled, middleware) = Build(ctx =>
        {
            Authenticate(ctx, userId);
            SetResolved(ctx, userId);
        });

        await middleware.InvokeAsync(context, db);

        Assert.False(nextCalled());
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        var body = await ReadBodyAsync(context);
        Assert.Contains("account_disabled", body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("account_disabled", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task AllowAnonymous_Endpoint_With_Disabled_Account_Calls_Next()
    {
        await using var db = BuildContext();
        var userId = Guid.NewGuid();
        db.Users.Add(CreateUser(userId, active: false));
        await db.SaveChangesAsync();

        var (context, nextCalled, middleware) = Build(ctx =>
        {
            Authenticate(ctx, userId);
            SetResolved(ctx, userId);
            SetAllowAnonymousEndpoint(ctx);
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled());
        Assert.NotEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_Request_Calls_Next()
    {
        await using var db = BuildContext();
        var userId = Guid.NewGuid();
        db.Users.Add(CreateUser(userId, active: false));
        await db.SaveChangesAsync();

        var (context, nextCalled, middleware) = Build(_ =>
        {
            // No authenticated principal, no resolved user.
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled());
        Assert.NotEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Disabled_Account_That_Matches_Enabled_PlatformAdministrator_By_UserId_Calls_Next_On_PlatformAdmin_Endpoint()
    {
        await using var db = BuildContext();
        var userId = Guid.NewGuid();
        db.Users.Add(CreateUser(userId, active: false));
        db.PlatformAdministrators.Add(PlatformAdministrator.Create(
            "admin@example.com", PlatformAdministratorRole.PlatformOwner, DateTimeOffset.UtcNow,
            supabaseAuthUserId: userId));
        await db.SaveChangesAsync();

        var (context, nextCalled, middleware) = Build(ctx =>
        {
            Authenticate(ctx, userId);
            SetResolved(ctx, userId, email: "admin@example.com");
            SetPlatformAdminEndpoint(ctx);
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled());
        Assert.NotEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Disabled_Account_That_Matches_Enabled_PlatformAdministrator_By_Email_Only_Calls_Next_On_PlatformAdmin_Endpoint()
    {
        await using var db = BuildContext();
        var userId = Guid.NewGuid();
        db.Users.Add(CreateUser(userId, active: false));
        db.PlatformAdministrators.Add(PlatformAdministrator.Create(
            "admin@example.com", PlatformAdministratorRole.SupportStaff, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var (context, nextCalled, middleware) = Build(ctx =>
        {
            Authenticate(ctx, userId);
            SetResolved(ctx, userId, email: "Admin@Example.com");
            SetPlatformAdminEndpoint(ctx);
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled());
        Assert.NotEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task PlatformAdministrator_With_Disabled_Account_Is_Gated_403_On_Company_Endpoint()
    {
        await using var db = BuildContext();
        var userId = Guid.NewGuid();
        db.Users.Add(CreateUser(userId, active: false));
        db.PlatformAdministrators.Add(PlatformAdministrator.Create(
            "admin@example.com", PlatformAdministratorRole.PlatformOwner, DateTimeOffset.UtcNow,
            supabaseAuthUserId: userId));
        await db.SaveChangesAsync();

        var (context, nextCalled, middleware) = Build(ctx =>
        {
            Authenticate(ctx, userId);
            SetResolved(ctx, userId, email: "admin@example.com");
            SetCompanyEndpoint(ctx);
        });

        await middleware.InvokeAsync(context, db);

        Assert.False(nextCalled());
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        var body = await ReadBodyAsync(context);
        Assert.Equal("account_disabled", JsonDocument.Parse(body).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Disabled_Account_With_Only_A_Disabled_PlatformAdministrator_Row_Returns_403()
    {
        await using var db = BuildContext();
        var userId = Guid.NewGuid();
        db.Users.Add(CreateUser(userId, active: false));
        var admin = PlatformAdministrator.Create(
            "admin@example.com", PlatformAdministratorRole.SupportStaff, DateTimeOffset.UtcNow,
            supabaseAuthUserId: userId);
        admin.Disable(DateTimeOffset.UtcNow, actorUserId: null);
        db.PlatformAdministrators.Add(admin);
        await db.SaveChangesAsync();

        var (context, nextCalled, middleware) = Build(ctx =>
        {
            Authenticate(ctx, userId);
            SetResolved(ctx, userId, email: "admin@example.com");
        });

        await middleware.InvokeAsync(context, db);

        Assert.False(nextCalled());
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Re_Enabled_Account_Calls_Next()
    {
        await using var db = BuildContext();
        var userId = Guid.NewGuid();
        var user = CreateUser(userId, active: false);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Simulate re-enablement.
        user.Reactivate(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var (context, nextCalled, middleware) = Build(ctx =>
        {
            Authenticate(ctx, userId);
            SetResolved(ctx, userId);
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled());
        Assert.NotEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_But_No_Resolved_User_Id_And_No_Account_Row_Calls_Next()
    {
        await using var db = BuildContext();

        var (context, nextCalled, middleware) = Build(ctx =>
        {
            Authenticate(ctx);
            SetResolved(ctx, userId: null, email: "someone@example.com");
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled());
        Assert.NotEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }
}
