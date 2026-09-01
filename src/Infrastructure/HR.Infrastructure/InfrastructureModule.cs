using Hangfire;
using Hangfire.PostgreSql;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.BackgroundJobs;
using HR.Infrastructure.Email;
using HR.Infrastructure.Persistence;
using HR.Infrastructure.Reporting;
using HR.Infrastructure.Storage;
using HR.SharedKernel;
using QuestPDF.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure;

public static class InfrastructureModule
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        AddEmailSender(services, configuration);
        services.AddSingleton<IInviteLinkBuilder, ConfiguredInviteLinkBuilder>();
        services.AddScoped<IAuditEventPublisher, DbAuditEventPublisher>();
        services.AddScoped<IAuditHistoryReader, AuditHistoryReader>();
        services.AddScoped<IAuditDataExportSource, Persistence.AuditDataExportSource>();
        services.AddScoped<AuditPendingItemPromotionJob>();
        services.AddSingleton<IRecurringJobRegistrar, AuditJobRegistrar>();
        services.AddDbContext<AuditDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "audit");
                npgsql.MigrationsAssembly(typeof(AuditDbContext).Assembly.GetName().Name!);
            }));

        services.AddHttpContextAccessor();
        services.AddHttpClient();
        AddProfilePhotoStorageService(services, configuration);
        AddSupportAttachmentStorageService(services, configuration);
        AddOrganisationDataExportStorage(services, configuration);

        QuestPDF.Settings.License = LicenseType.Community;
        services.AddScoped<IReportExporter, ReportExporter>();

        // System Health Dashboard (Platform Monitoring epic) — "email" (live Postmark reachability
        // probe) and "storage" (live Supabase Storage reachability probe) named health checks.
        services.Configure<PostmarkOptions>(configuration.GetSection("Infrastructure:Postmark"));
        services.Configure<SupabaseProfilePhotoStorageOptions>(configuration.GetSection("Infrastructure:Supabase:ProfilePhotos"));
        services.AddHealthChecks()
            // NFR-03: email (Postmark) and file storage (Supabase Storage) are degraded (optional)
            // dependencies — losing them impairs specific features but must not take the platform
            // offline, so they are not tagged "critical" and never cause /health/ready to 503.
            .AddCheck<PostmarkHealthCheck>("email", tags: ["degraded"])
            .AddCheck<SupabaseStorageHealthCheck>("storage", tags: ["degraded"]);

        return services;
    }

    private static void AddProfilePhotoStorageService(IServiceCollection services, IConfiguration configuration)
    {
        var supabaseSection = configuration.GetSection("Infrastructure:Supabase:ProfilePhotos");

        if (supabaseSection.Exists() && !string.IsNullOrWhiteSpace(supabaseSection["SupabaseUrl"]))
        {
            services.Configure<SupabaseProfilePhotoStorageOptions>(supabaseSection);
            services.AddHttpClient<IProfilePhotoStorageService, SupabaseProfilePhotoStorageService>();
        }
        else
        {
            services.AddScoped<IProfilePhotoStorageService, LocalProfilePhotoStorageService>();
        }
    }

    private static void AddSupportAttachmentStorageService(IServiceCollection services, IConfiguration configuration)
    {
        var supabaseSection = configuration.GetSection("Infrastructure:Supabase:SupportAttachments");

        if (supabaseSection.Exists() && !string.IsNullOrWhiteSpace(supabaseSection["SupabaseUrl"]))
        {
            services.Configure<SupabaseSupportAttachmentStorageOptions>(supabaseSection);
            services.AddHttpClient<ISupportAttachmentStorageService, SupabaseSupportAttachmentStorageService>();
        }
        else
        {
            services.AddScoped<ISupportAttachmentStorageService, LocalSupportAttachmentStorageService>();
        }
    }

    private static void AddOrganisationDataExportStorage(IServiceCollection services, IConfiguration configuration)
    {
        var supabaseSection = configuration.GetSection("Infrastructure:Supabase:OrganisationExports");

        if (supabaseSection.Exists() && !string.IsNullOrWhiteSpace(supabaseSection["SupabaseUrl"]))
        {
            services.Configure<SupabaseOrganisationDataExportStorageOptions>(supabaseSection);
            services.AddHttpClient<IOrganisationDataExportStorage, SupabaseOrganisationDataExportStorage>();
        }
        else
        {
            services.AddScoped<IOrganisationDataExportStorage, LocalOrganisationDataExportStorage>();
        }
    }

    private static void AddEmailSender(IServiceCollection services, IConfiguration configuration)
    {
        var postmarkSection = configuration.GetSection("Infrastructure:Postmark");

        if (postmarkSection.Exists() && !string.IsNullOrWhiteSpace(postmarkSection["ServerToken"]))
        {
            services.Configure<PostmarkOptions>(postmarkSection);
            services.Configure<EmailBrandingOptions>(configuration.GetSection("EmailBranding"));
            services.AddHttpClient<IEmailSender, PostmarkEmailSender>();
            services.AddHttpClient<IInvitationEmailSender, PostmarkInvitationEmailSender>();
            services.AddHttpClient<IPasswordResetEmailSender, PostmarkPasswordResetEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
            services.AddSingleton<IInvitationEmailSender, LoggingInvitationEmailSender>();
            services.AddSingleton<IPasswordResetEmailSender, LoggingPasswordResetEmailSender>();
        }
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
            // NFR-03: background processing is a degraded (optional) dependency for request
            // serving — if Hangfire is down, jobs queue up but the web/API surface stays available.
            .AddCheck<HangfireHealthCheck>("hangfire", tags: ["degraded"]);

        services.AddScoped<IBackgroundJobStatusReader, HangfireJobStatusReader>();

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
