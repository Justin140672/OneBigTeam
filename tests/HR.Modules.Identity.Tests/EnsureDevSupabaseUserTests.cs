using HR.Modules.Identity;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// The Development-only "make this employee login-ready" provisioning path
/// (IdentityModule.EnsureDevSupabaseUserAsync, behind POST /api/dev/ensure-employee-login) must
/// leave the user in the same shape as a seeded dev persona — including the ApplicationUser row.
/// Without it, user-administration endpoints that load db.Users (e.g. UpdateUserRoles) answered
/// "User was not found." for the provisioned user.
/// </summary>
public class EnsureDevSupabaseUserTests
{
    private static ServiceProvider BuildServices()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<IdentityDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddSingleton<ISupabaseAuthGateway>(new HR.Modules.Identity.Tests.Infrastructure.FakeSupabaseAuthGateway());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Creates_ApplicationUser_Profile_And_EmployeeRole_UnderTheEmployeeId()
    {
        await using var services = BuildServices();
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await services.EnsureDevSupabaseUserAsync(id, companyId, "e2e.someone@acme.example", "E2E", "Someone", CancellationToken.None);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == id);
        Assert.Equal("E2E.SOMEONE@ACME.EXAMPLE", user.NormalizedEmail);
        Assert.True(await db.UserProfiles.AnyAsync(p => p.Id == id));
        Assert.True(await db.UserRoles.AnyAsync(r => r.UserId == id && r.RoleId == SystemRoles.Employee));
    }

    [Fact]
    public async Task Is_Idempotent()
    {
        await using var services = BuildServices();
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await services.EnsureDevSupabaseUserAsync(id, companyId, "e2e.twice@acme.example", "E2E", "Twice", CancellationToken.None);
        await services.EnsureDevSupabaseUserAsync(id, companyId, "e2e.twice@acme.example", "E2E", "Twice", CancellationToken.None);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Id == id));
    }
}
