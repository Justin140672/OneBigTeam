using System.Security.Claims;

using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Tests.Infrastructure;

using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// Unit tests for <see cref="PlatformAdminAuthorizationHandler"/> (SEC-002's real DB-backed
/// enforcement point for the "platform:admin" policy). The critical case here is the P1
/// "Login as Customer" anti-escalation guardrail: a support session's token carries the acting
/// admin's own real email (for display/audit) and would otherwise satisfy this policy's
/// email-fallback lookup — it must be denied regardless.
/// </summary>
[Collection("IdentityDatabase")]
public class PlatformAdminAuthorizationHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    private static async Task<AuthorizationHandlerContext> RunAsync(
        PlatformAdminAuthorizationHandler handler)
    {
        var context = new AuthorizationHandlerContext([new PlatformAdminRequirement()], new ClaimsPrincipal(), resource: null);
        await handler.HandleAsync(context);
        return context;
    }

    [Fact]
    public async Task SupportSession_Is_Denied_Even_When_Its_Email_Matches_A_Real_Enabled_PlatformAdministrator()
    {
        var email = $"real-owner-{Guid.NewGuid():N}@test.com";
        await using (var seedDb = fixture.BuildContext())
        {
            seedDb.PlatformAdministrators.Add(
                PlatformAdministrator.Create(email, PlatformAdministratorRole.PlatformOwner, Now));
            await seedDb.SaveChangesAsync();
        }

        var handler = new PlatformAdminAuthorizationHandler(
            FakeCurrentUser.SupportSession(Guid.NewGuid(), email: email),
            fixture.BuildContext());

        var context = await RunAsync(handler);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Non_SupportSession_User_Succeeds_When_Email_Matches_A_Real_Enabled_PlatformAdministrator()
    {
        var email = $"real-owner-{Guid.NewGuid():N}@test.com";
        await using (var seedDb = fixture.BuildContext())
        {
            seedDb.PlatformAdministrators.Add(
                PlatformAdministrator.Create(email, PlatformAdministratorRole.PlatformOwner, Now));
            await seedDb.SaveChangesAsync();
        }

        var handler = new PlatformAdminAuthorizationHandler(
            new FakeCurrentUser(Guid.NewGuid(), email),
            fixture.BuildContext());

        var context = await RunAsync(handler);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task SupportSession_Is_Denied_Even_When_UserId_Is_Null_And_Only_Email_Is_Present()
    {
        var email = $"real-owner-{Guid.NewGuid():N}@test.com";
        await using (var seedDb = fixture.BuildContext())
        {
            seedDb.PlatformAdministrators.Add(
                PlatformAdministrator.Create(email, PlatformAdministratorRole.PlatformOwner, Now));
            await seedDb.SaveChangesAsync();
        }

        var handler = new PlatformAdminAuthorizationHandler(
            new FakeCurrentUser(null, email, isAuthenticated: true, isSupportSession: true),
            fixture.BuildContext());

        var context = await RunAsync(handler);

        Assert.False(context.HasSucceeded);
    }
}
