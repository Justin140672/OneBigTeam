using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.Login;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class LoginHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTime Now = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FakeClock Clock = new(Now);

    private ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(fixture.ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "identity")));
        services.AddSingleton<IClock>(Clock);
        return services.BuildServiceProvider();
    }

    private LoginHandler BuildHandler(FakeSupabaseAuthGateway gateway, ServiceProvider serviceProvider) =>
        new(
            gateway,
            fixture.BuildContext(),
            serviceProvider,
            new IdentityAuthorizationService(fixture.BuildContext(), Clock),
            NullLogger<LoginHandler>.Instance);

    [Fact]
    public async Task HandleAsync_Returns_Success_For_User_With_A_Role()
    {
        var supabaseUserId = Guid.NewGuid();
        var email = $"login-{Guid.NewGuid():N}@example.com";

        await using (var db = fixture.BuildContext())
        {
            var profile = UserProfile.Create(
                Guid.NewGuid(), supabaseUserId, Guid.NewGuid(), email, "Ada", "Lovelace", Now);
            db.UserProfiles.Add(profile);
            db.UserRoles.Add(UserRole.Create(profile.Id, SystemRoles.Employee, Now));
            await db.SaveChangesAsync();
        }

        var gateway = new FakeSupabaseAuthGateway { UserIdToReturn = supabaseUserId };
        await using var serviceProvider = BuildServiceProvider();
        var handler = BuildHandler(gateway, serviceProvider);

        var result = await handler.HandleAsync(new LoginRequest(email, "whatever-password"), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// A UserProfile can exist with a real, working Supabase Auth identity but hold no roles at
    /// all in this app — e.g. an account that's actually a platform administrator (Admin
    /// Portal-only, see PlatformAdministrator) rather than a real company-app user. Without this
    /// rejection, such an account would "successfully" log in to HR.Web with no roles to land on
    /// any page with, rather than a clear "you can't use this app" outcome.
    /// </summary>
    [Fact]
    public async Task HandleAsync_Returns_Failure_For_User_With_No_Roles()
    {
        var supabaseUserId = Guid.NewGuid();
        var email = $"no-roles-{Guid.NewGuid():N}@example.com";

        await using (var db = fixture.BuildContext())
        {
            db.UserProfiles.Add(UserProfile.Create(
                Guid.NewGuid(), supabaseUserId, Guid.NewGuid(), email, "No", "Roles", Now));
            await db.SaveChangesAsync();
        }

        var gateway = new FakeSupabaseAuthGateway { UserIdToReturn = supabaseUserId };
        await using var serviceProvider = BuildServiceProvider();
        var handler = BuildHandler(gateway, serviceProvider);

        var result = await handler.HandleAsync(new LoginRequest(email, "whatever-password"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Invalid email or password.", result.Error.Message);
    }

    [Fact]
    public async Task HandleAsync_Returns_Failure_When_Supabase_SignIn_Fails()
    {
        var gateway = new FakeSupabaseAuthGateway { ShouldThrowOnSignIn = true };
        await using var serviceProvider = BuildServiceProvider();
        var handler = BuildHandler(gateway, serviceProvider);

        var result = await handler.HandleAsync(
            new LoginRequest("nobody@example.com", "wrong-password"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Invalid email or password.", result.Error.Message);
    }

    [Fact]
    public async Task HandleAsync_Returns_Failure_When_No_UserProfile_Matches_Supabase_User()
    {
        var gateway = new FakeSupabaseAuthGateway { UserIdToReturn = Guid.NewGuid() };
        await using var serviceProvider = BuildServiceProvider();
        var handler = BuildHandler(gateway, serviceProvider);

        var result = await handler.HandleAsync(
            new LoginRequest("orphaned@example.com", "whatever-password"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Invalid email or password.", result.Error.Message);
    }
}
