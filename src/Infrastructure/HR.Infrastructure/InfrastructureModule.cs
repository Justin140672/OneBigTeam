using Hangfire;
using Hangfire.PostgreSql;
using HR.Infrastructure.BackgroundJobs;
using HR.Infrastructure.Email;
using HR.Infrastructure.Persistence;
using HR.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

    public static IServiceCollection AddHangfireBackgroundJobs(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options =>
                options.UseNpgsqlConnection(connectionString)));

        services.AddHangfireServer(options =>
        {
            options.Queues = ["critical", "default", "low"];
        });

        return services;
    }

    public static WebApplication UseHangfireBackgroundJobs(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseHangfireDashboard("/hangfire", new DashboardOptions
            {
                Authorization = [],
            });
        }

        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        foreach (var registrar in app.Services.GetServices<IRecurringJobRegistrar>())
            registrar.Register(jobManager);

        return app;
    }

    public static async Task MigrateAuditAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS audit");
        await db.Database.MigrateAsync();
    }
}
