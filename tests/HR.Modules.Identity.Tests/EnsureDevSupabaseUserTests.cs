using HR.Modules.Identity;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Identity.Tests;

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
    public async Task Creates_Profile_And_EmployeeRole_UnderTheEmployeeId()
    {
        await using var services = BuildServices();
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await services.EnsureDevSupabaseUserAsync(id, companyId, "e2e.someone@acme.example", "E2E", "Someone", CancellationToken.None);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var profile = await db.UserProfiles.SingleAsync(p => p.Id == id);
        Assert.Equal("e2e.someone@acme.example", profile.Email);
        Assert.Equal(companyId, profile.CompanyId);
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
        Assert.Equal(1, await db.UserProfiles.CountAsync(u => u.Id == id));
    }
}
