using Hangfire;
using Hangfire.PostgreSql;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.BackgroundJobs;
using HR.Infrastructure.Email;
using HR.Infrastructure.Persistence;
using HR.Infrastructure.Reporting;
using HR.Infrastructure.Security;
using HR.Infrastructure.Storage;
using HR.SharedKernel;
using QuestPDF.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure;

public static class InfrastructureModule
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Ticket 23 (P2): ambient execution-context accessor (correlation/causation/message ids) -
        // stateless wrapper over a static AsyncLocal, so singleton lifetime is correct and it is
        // visible to both DI-constructed classes and static extension methods (e.g. outbox helpers).
        services.AddSingleton<HR.SharedKernel.ExecutionContext.IExecutionContextAccessor,
            HR.SharedKernel.ExecutionContext.ExecutionContextAccessor>();

        AddEmailSender(services, configuration, environment);
        services.AddSingleton<IInviteLinkBuilder, ConfiguredInviteLinkBuilder>();
        services.AddScoped<IAuditEventPublisher, DbAuditEventPublisher>();
        services.AddSingleton<ISupportSessionTokenIssuer, Security.SupportSessionTokenIssuer>();
        services.AddScoped<IAuditHistoryReader, AuditHistoryReader>();
        services.AddScoped<IAuditEventExistenceReader, AuditEventExistenceReader>();
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
        AddSensitiveDataProtection(services, configuration);
        AddProfilePhotoStorageService(services, configuration, environment);
        AddSupportAttachmentStorageService(services, configuration, environment);
        AddOrganisationDataExportStorage(services, configuration, environment);

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

        // Time services for company-local date resolution and testing
        services.AddScoped<IClockProvider, SystemClockProvider>();
        services.AddScoped<ICompanyTimeProvider, CompanyTimeProvider>();

        return services;
    }

    private static void AddSensitiveDataProtection(IServiceCollection services, IConfiguration configuration)
    {
        // Ticket 1: application-level AES-256-GCM protection for sensitive persisted values.
        // Keys are bound from environment/secret configuration only; the protector is built lazily so
        // that environments which do not yet persist any protected field are not forced to configure
        // keys. The first use of ISensitiveDataProtector without valid key config fails fast with a
        // SensitiveDataProtectionException.
        services.Configure<SensitiveDataProtectionOptions>(
            configuration.GetSection(SensitiveDataProtectionOptions.SectionName));
        services.AddSingleton<ISensitiveDataProtector>(sp =>
            AesGcmSensitiveDataProtector.Create(
                sp.GetRequiredService<IOptions<SensitiveDataProtectionOptions>>().Value));

        // Ticket 9: a safe readiness signal that encryption is configured and the active key works.
        // Tagged "ready" only (not "critical") — the hard production gate is
        // ValidateSensitiveDataProtectionOrThrow, called from the API host on non-Development startup.
        services.AddHealthChecks()
            .AddCheck<SensitiveDataProtectionHealthCheck>(
                "sensitive-data-encryption", tags: ["ready"]);
    }

    /// <summary>
    /// Ticket 9 — operational safety. Fails fast (throws, crashing startup) when sensitive-data
    /// encryption is absent or invalid. Call this from the application host during startup in every
    /// environment where encrypted data is expected (production and staging). It resolves the
    /// protector — which runs <c>AesGcmSensitiveDataProtector.Create</c> key validation and never
    /// silently generates a replacement key — and proves the active key with a fixed non-sensitive
    /// round-trip. Any failure is a deliberate hard stop: starting with broken or missing encryption
    /// config risks writing plaintext or permanently losing access to encrypted customer data.
    /// The thrown <see cref="SensitiveDataProtectionException"/> message never contains key material.
    /// </summary>
    public static void ValidateSensitiveDataProtectionOrThrow(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var protector = services.GetRequiredService<ISensitiveDataProtector>();
        const string sentinel = "obt-sensitive-data-encryption-startup-selftest";
        var token = protector.Protect(sentinel);
        if (protector.Unprotect(token) != sentinel)
            throw new SensitiveDataProtectionException(
                "Sensitive-data encryption self-test failed during startup: encrypt/decrypt round-trip mismatch.");
    }

    /// <summary>
    /// Security review ticket 3 (P1): a missing Supabase storage config category must never silently
    /// activate an ephemeral local (temp-dir) fallback outside Development/an explicit test
    /// environment — that data disappears on restart/redeploy, and the local download route is
    /// dev-only. Shared by all three <c>Local*StorageService</c> categories below (Documents' own
    /// Supabase-vs-local switch in DocumentsModule follows the same rule for the same reason).
    /// </summary>
    private static bool IsLocalStorageAllowedEnvironment(IHostEnvironment environment) =>
        environment.IsDevelopment()
        || environment.IsEnvironment("Test")
        || string.Equals(Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase);

    private static void AddProfilePhotoStorageService(
        IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // The options validator resolves IHostEnvironment via constructor injection to gate the
        // Development/Test-only HTTP allowance (security review finding 6). The host already
        // registers IHostEnvironment in production; TryAddSingleton is a no-op there and only
        // matters for tests that build a bare IServiceCollection.
        services.TryAddSingleton(environment);

        var supabaseSection = configuration.GetSection("Infrastructure:Supabase:ProfilePhotos");

        if (supabaseSection.Exists() && !string.IsNullOrWhiteSpace(supabaseSection["SupabaseUrl"]))
        {
            services.AddOptions<SupabaseProfilePhotoStorageOptions>().Bind(supabaseSection).ValidateOnStart();
            services.AddSingleton<IValidateOptions<SupabaseProfilePhotoStorageOptions>, SupabaseProfilePhotoStorageOptionsValidator>();
            services.AddHttpClient<IProfilePhotoStorageService, SupabaseProfilePhotoStorageService>();
        }
        else if (IsLocalStorageAllowedEnvironment(environment))
        {
            services.AddScoped<IProfilePhotoStorageService, LocalProfilePhotoStorageService>();
            services.TryAddSingleton<ILocalStorageUrlSigner>(_ => new LocalStorageUrlSigner(TimeProvider.System));
        }
        else
        {
            throw new InvalidOperationException(
                "Profile photo storage is not configured for this environment. "
                + "'Infrastructure:Supabase:ProfilePhotos:SupabaseUrl' (and ServiceRoleKey/BucketName) "
                + "must be set in Staging/Production — the local temp-directory fallback is only "
                + "permitted in Development or an explicit automated-test environment.");
        }
    }

    private static void AddSupportAttachmentStorageService(
        IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.TryAddSingleton(environment);

        var supabaseSection = configuration.GetSection("Infrastructure:Supabase:SupportAttachments");

        if (supabaseSection.Exists() && !string.IsNullOrWhiteSpace(supabaseSection["SupabaseUrl"]))
        {
            services.AddOptions<SupabaseSupportAttachmentStorageOptions>().Bind(supabaseSection).ValidateOnStart();
            services.AddSingleton<IValidateOptions<SupabaseSupportAttachmentStorageOptions>, SupabaseSupportAttachmentStorageOptionsValidator>();
            services.AddHttpClient<ISupportAttachmentStorageService, SupabaseSupportAttachmentStorageService>();
            services.AddHealthChecks().AddCheck<SupabaseSupportAttachmentStorageHealthCheck>(
                "support-attachment-storage", tags: ["degraded"]);
        }
        else if (IsLocalStorageAllowedEnvironment(environment))
        {
            services.AddScoped<ISupportAttachmentStorageService, LocalSupportAttachmentStorageService>();
            services.TryAddSingleton<ILocalStorageUrlSigner>(_ => new LocalStorageUrlSigner(TimeProvider.System));
        }
        else
        {
            throw new InvalidOperationException(
                "Support attachment storage is not configured for this environment. "
                + "'Infrastructure:Supabase:SupportAttachments:SupabaseUrl' (and ServiceRoleKey/BucketName) "
                + "must be set in Staging/Production — the local temp-directory fallback is only "
                + "permitted in Development or an explicit automated-test environment.");
        }
    }

    private static void AddOrganisationDataExportStorage(
        IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.TryAddSingleton(environment);

        var supabaseSection = configuration.GetSection("Infrastructure:Supabase:OrganisationExports");

        if (supabaseSection.Exists() && !string.IsNullOrWhiteSpace(supabaseSection["SupabaseUrl"]))
        {
            services.AddOptions<SupabaseOrganisationDataExportStorageOptions>().Bind(supabaseSection).ValidateOnStart();
            services.AddSingleton<IValidateOptions<SupabaseOrganisationDataExportStorageOptions>, SupabaseOrganisationDataExportStorageOptionsValidator>();
            services.AddHttpClient<IOrganisationDataExportStorage, SupabaseOrganisationDataExportStorage>();
            services.AddHealthChecks().AddCheck<SupabaseOrganisationDataExportStorageHealthCheck>(
                "organisation-export-storage", tags: ["degraded"]);
        }
        else if (IsLocalStorageAllowedEnvironment(environment))
        {
            services.AddScoped<IOrganisationDataExportStorage, LocalOrganisationDataExportStorage>();
        }
        else
        {
            throw new InvalidOperationException(
                "Organisation data export storage is not configured for this environment. "
                + "'Infrastructure:Supabase:OrganisationExports:SupabaseUrl' (and ServiceRoleKey/BucketName) "
                + "must be set in Staging/Production — the local temp-directory fallback is only "
                + "permitted in Development or an explicit automated-test environment.");
        }
    }

    /// <summary>
    /// Security review ticket 5 (P1): the logging-only email senders (<see cref="LoggingEmailSender"/>
    /// / <see cref="LoggingInvitationEmailSender"/> / <see cref="LoggingPasswordResetEmailSender"/>)
    /// unconditionally report every send as delivered (they return <c>true</c>/complete without ever
    /// talking to a real provider). That is only acceptable where nothing downstream can mistake it
    /// for a genuine delivery confirmation: Development, the "Test" environment name used by
    /// WebApplicationFactory-hosted integration tests, or the explicit E2E harness (same
    /// E2E_TESTING flag already used to keep the Playwright suite off the real Postmark API — see
    /// the remarks below). Staging/Production MUST have a fully configured Postmark provider; a
    /// missing token, sender identity, message stream, or template alias there is a deliberate hard
    /// stop, exactly like the Ticket 2/3 malware-scanning and storage fail-closed checks this
    /// mirrors — starting up with transactional email silently no-op'd risks invitations, password
    /// resets, and support notifications that are never actually delivered while every call site
    /// believes they were sent.
    /// </summary>
    private static void AddEmailSender(
        IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var postmarkSection = configuration.GetSection("Infrastructure:Postmark");

        // Never wire the live Postmark senders into the Playwright E2E run. That suite boots the real
        // AppHost with ASPNETCORE_ENVIRONMENT=Development against seeded *.example / *.betacorp.example
        // personas, so a configured server token would fire real Postmark API calls to reserved-domain
        // addresses — guaranteed hard bounces that degrade the sending domain's reputation. Mirrors the
        // existing E2E_TESTING swaps for Stripe (E2eStripeGateway) and Supabase auth (FakeSupabaseAuthGateway).
        var e2eTesting = string.Equals(
            Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase);

        // Note: deliberately NOT including e2eTesting here. E2E_TESTING is only ever legitimately
        // set under Development (HR.Api/Program.cs refuses to start otherwise), so by the time this
        // runs for real it always coincides with IsDevelopment(). Keeping the flag out of this
        // predicate means a hypothetical non-Development host with E2E_TESTING set still fails
        // closed rather than getting a free pass into the logging stub.
        var isLoggingSenderAllowedEnvironment = environment.IsDevelopment() || environment.IsEnvironment("Test");

        var missingSettings = GetMissingPostmarkSettings(postmarkSection);
        var isFullyConfigured = missingSettings.Count == 0;

        if (isFullyConfigured && !e2eTesting)
        {
            services.Configure<PostmarkOptions>(postmarkSection);
            services.Configure<EmailBrandingOptions>(configuration.GetSection("EmailBranding"));
            services.AddHttpClient<IEmailSender, PostmarkEmailSender>();
            services.AddHttpClient<IInvitationEmailSender, PostmarkInvitationEmailSender>();
            services.AddHttpClient<IPasswordResetEmailSender, PostmarkPasswordResetEmailSender>();
        }
        else if (isLoggingSenderAllowedEnvironment)
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
            services.AddSingleton<IInvitationEmailSender, LoggingInvitationEmailSender>();
            services.AddSingleton<IPasswordResetEmailSender, LoggingPasswordResetEmailSender>();
        }
        else
        {
            throw new InvalidOperationException(
                "Transactional email is not fully configured for this environment. Missing: "
                + string.Join(", ", missingSettings)
                + ". The logging-only stub senders (which report every send as delivered without "
                + "ever contacting a real provider) are only permitted in Development or an "
                + "explicit automated-test environment — Staging/Production must have a fully "
                + "configured Postmark provider under 'Infrastructure:Postmark'.");
        }
    }

    private static IReadOnlyList<string> GetMissingPostmarkSettings(IConfigurationSection postmarkSection)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(postmarkSection["ServerToken"]))
            missing.Add("Infrastructure:Postmark:ServerToken");
        if (string.IsNullOrWhiteSpace(postmarkSection["FromEmail"]))
            missing.Add("Infrastructure:Postmark:FromEmail");
        if (string.IsNullOrWhiteSpace(postmarkSection["MessageStream"]))
            missing.Add("Infrastructure:Postmark:MessageStream");
        if (string.IsNullOrWhiteSpace(postmarkSection["InvitationTemplateAlias"]))
            missing.Add("Infrastructure:Postmark:InvitationTemplateAlias");
        if (string.IsNullOrWhiteSpace(postmarkSection["PasswordResetTemplateAlias"]))
            missing.Add("Infrastructure:Postmark:PasswordResetTemplateAlias");

        return missing;
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
                app.Services.GetRequiredService<IServiceScopeFactory>(),
                app.Services.GetRequiredService<ILogger<BackgroundJobAuditFilter>>()));

        // Security review ticket 6 (P2): this endpoint discloses infrastructure detail (server names,
        // queue depths, and — on failure — raw exception messages) that must not be exposed to an
        // anonymous caller. Gated behind the same HealthChecks:ReadinessDetailToken already required
        // for full /health/ready detail (Microsoft.Extensions.Hosting.HealthCheckEndpoints), checked
        // manually (not via [Authorize]/RequireAuthorization) so this stays correct even if it is ever
        // reached from a reduced pipeline with no auth middleware installed, exactly like
        // /health/startup-migrations.
        var backgroundJobsHealthLogger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("HR.Infrastructure.BackgroundJobsHealthEndpoint");

        app.MapGet("/health/background-jobs", (HttpContext httpContext, JobStorage jobStorage) =>
        {
            var logger = backgroundJobsHealthLogger;

            if (!Microsoft.Extensions.Hosting.HealthCheckEndpoints.HasDetailAccess(httpContext))
            {
                return Results.Json(new { status = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
            }

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
                // The raw exception is logged internally only; the response never echoes ex.Message.
                logger.LogError(ex, "Background job health check failed while querying Hangfire monitoring API");
                return Results.Json(new { status = "unhealthy", checkedAt = DateTimeOffset.UtcNow },
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
