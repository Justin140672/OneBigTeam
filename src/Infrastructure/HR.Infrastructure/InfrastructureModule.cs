using HR.Infrastructure.Email;
using HR.Infrastructure.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Infrastructure;

public static class InfrastructureModule
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IEmailSender, LoggingEmailSender>();
        services.AddSingleton<IInviteLinkBuilder, ConfiguredInviteLinkBuilder>();
        services.AddScoped<IAuditEventPublisher, DbAuditEventPublisher>();
        services.AddDbContext<AuditDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "audit");
                npgsql.MigrationsAssembly(typeof(AuditDbContext).Assembly.GetName().Name!);
            }));
        return services;
    }

    public static async Task MigrateAuditAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS audit");
        await db.Database.MigrateAsync();
    }
}
