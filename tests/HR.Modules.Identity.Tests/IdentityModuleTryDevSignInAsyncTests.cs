using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Identity.Tests;

// Ticket 1 (P1): TryDevSignInAsync is the real Login handler's only account-status gate (see
// Features/Login/Handler.cs and the class remarks on IdentityModule.TryDevSignInAsync). It must
// also block sign-in for UserProfile-only accounts (AcceptInvite, self-service SignUp), not just
// legacy ApplicationUser-backed ones — previously a disabled invited/signed-up user could keep
// signing in indefinitely.
[Collection("IdentityDatabase")]
public class IdentityModuleTryDevSignInAsyncTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    private IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(fixture.ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "identity")));
        services.AddSingleton<IClock>(new FakeClock(Now.UtcDateTime));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Returns_False_For_Disabled_UserProfile_Only_Account()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await using (var db = fixture.BuildContext())
        {
            var profile = UserProfile.Create(
                userId, Guid.NewGuid(), companyId, $"disabled-{userId}@test.com", "Test", "User", Now);
            profile.Deactivate(Now);
            db.UserProfiles.Add(profile);
            await db.SaveChangesAsync();
        }

        var services = BuildServices();

        var allowed = await services.TryDevSignInAsync(userId);

        Assert.False(allowed);
    }

    [Fact]
    public async Task Returns_True_For_Active_UserProfile_Only_Account()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await using (var db = fixture.BuildContext())
        {
            db.UserProfiles.Add(UserProfile.Create(
                userId, Guid.NewGuid(), companyId, $"active-{userId}@test.com", "Test", "User", Now));
            await db.SaveChangesAsync();
        }

        var services = BuildServices();

        var allowed = await services.TryDevSignInAsync(userId);

        Assert.True(allowed);
    }

    [Fact]
    public async Task Returns_True_When_No_ApplicationUser_Or_UserProfile_Row_Exists()
    {
        var services = BuildServices();

        var allowed = await services.TryDevSignInAsync(Guid.NewGuid());

        Assert.True(allowed);
    }

    [Fact]
    public async Task Returns_False_For_Disabled_ApplicationUser_Account()
    {
        var userId = Guid.NewGuid();
        await using (var db = fixture.BuildContext())
        {
            var user = ApplicationUser.Create(userId, $"disabled-{userId}@test.com", "hash", "Test", "User", Now);
            user.Deactivate(Now);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var services = BuildServices();

        var allowed = await services.TryDevSignInAsync(userId);

        Assert.False(allowed);
    }

    [Fact]
    public async Task Prefers_ApplicationUser_Over_UserProfile_When_Both_Exist()
    {
        // ApplicationUser is active, UserProfile (same id) is disabled — TryDevSignInAsync checks
        // Users first and returns as soon as it finds a row there, so the disabled profile must not
        // block sign-in.
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(userId, $"both-{userId}@test.com", "hash", "Test", "User", Now));
            var profile = UserProfile.Create(
                userId, Guid.NewGuid(), companyId, $"both-{userId}@test.com", "Test", "User", Now);
            profile.Deactivate(Now);
            db.UserProfiles.Add(profile);
            await db.SaveChangesAsync();
        }

        var services = BuildServices();

        var allowed = await services.TryDevSignInAsync(userId);

        Assert.True(allowed);
    }
}
