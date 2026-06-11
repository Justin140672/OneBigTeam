using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Leave;

public static class LeaveModule
{
    public static IServiceCollection AddLeaveModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<LeaveDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "leave")));

        return services;
    }

    public static async Task MigrateLeaveAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS leave");
        await db.Database.MigrateAsync();
    }
}
