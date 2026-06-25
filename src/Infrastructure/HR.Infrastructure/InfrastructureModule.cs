using Hangfire;
using Hangfire.PostgreSql;
using HR.Infrastructure.BackgroundJobs;
using HR.Infrastructure.Email;
using HR.Infrastructure.Persistence;
using HR.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

        services.AddHealthChecks()
            .AddCheck<HangfireHealthCheck>("hangfire", tags: ["ready"]);

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

        GlobalJobFilters.Filters.Add(
            new BackgroundJobLoggingFilter(
                app.Services.GetRequiredService<ILogger<BackgroundJobLoggingFilter>>()));

        GlobalJobFilters.Filters.Add(
            new BackgroundJobAuditFilter(
                app.Services.GetRequiredService<IServiceScopeFactory>()));

        app.MapGet("/health/background-jobs", (JobStorage jobStorage) =>
        {
            try
            {
                var api = jobStorage.GetMonitoringApi();
                var servers = api.Servers();
                var queues = api.Queues();
                var stats = api.GetStatistics();

                var response = new
                {
                    status = servers.Count == 0 ? "unhealthy"
                           : stats.Failed > 0    ? "degraded"
                           : "healthy",
                    servers = servers.Select(s => new
                    {
                        name = s.Name,
                        workers = s.WorkersCount,
                        queues = s.Queues,
                        startedAt = s.StartedAt,
                        heartbeat = s.Heartbeat,
                    }),
                    queues = queues.Select(q => new
                    {
                        name = q.Name,
                        length = q.Length,
                        fetched = q.Fetched,
                    }),
                    statistics = new
                    {
                        enqueued = stats.Enqueued,
                        processing = stats.Processing,
                        scheduled = stats.Scheduled,
                        failed = stats.Failed,
                        succeeded = stats.Succeeded,
                        recurring = stats.Recurring,
                    },
                    checkedAt = DateTimeOffset.UtcNow,
                };

                var statusCode = servers.Count == 0 || stats.Failed > 0
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status200OK;

                return Results.Json(response, statusCode: statusCode);
            }
            catch (Exception ex)
            {
                return Results.Json(new { status = "unhealthy", error = ex.Message, checkedAt = DateTimeOffset.UtcNow },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

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
