using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "identity")));
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddScoped<ICurrentTenant, HttpContextCurrentTenant>();
        services.AddScoped<IAuthorizationService, IdentityAuthorizationService>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }

    public static IApplicationBuilder UseIdentityModule(this IApplicationBuilder app)
    {
        app.UseMiddleware<SupabaseCurrentUserResolutionMiddleware>();
        app.UseMiddleware<RequireTenantMiddleware>();
        return app;
    }

    public static async Task MigrateIdentityAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS identity");
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// Seeds a development user with the HrAdministrator role so the DevAuthHandler
    /// has a valid, permission-bearing identity. Safe to call on every startup (idempotent).
    /// </summary>
    public static async Task SeedDevUserAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var devUserId = new Guid("00000000-0000-0000-0000-000000000099");

        var exists = await db.Users.AnyAsync(u => u.Id == devUserId);
        if (!exists)
        {
            db.Users.Add(ApplicationUser.Create(
                devUserId,
                email: "admin@dev.local",
                passwordHash: "dev-only-not-used",
                firstName: "Dev",
                lastName: "Admin",
                now: DateTimeOffset.UtcNow));
        }

        var roleExists = await db.UserRoles.AnyAsync(
            ur => ur.UserId == devUserId && ur.RoleId == SystemRoles.HrAdministrator);
        if (!roleExists)
        {
            db.UserRoles.Add(UserRole.Create(devUserId, SystemRoles.HrAdministrator, DateTimeOffset.UtcNow));
        }

        await db.SaveChangesAsync();
    }
}
