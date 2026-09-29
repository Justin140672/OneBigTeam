using HR.SharedKernel;
using FluentValidation;
using Hangfire;
using HR.Modules.DataImport.Features.ConfirmImportSession;
using HR.Modules.DataImport.Features.DownloadImportTemplate;
using HR.Modules.DataImport.Features.ExportImportErrors;
using HR.Modules.DataImport.Features.GetImportPreview;
using HR.Modules.DataImport.Features.GetImportSession;
using HR.Modules.DataImport.Features.GetImportSessionColumns;
using HR.Modules.DataImport.Features.ListImportSessions;
using HR.Modules.DataImport.Features.UploadImportFile;
using HR.Modules.DataImport.Features.ValidateImportSession;
using HR.Modules.DataImport.Jobs;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HR.Modules.DataImport;

public static class DataImportModule
{
    public static IServiceCollection AddDataImportModule(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<ImportFileUploadOptions>(configuration.GetSection("DataImport:FileUpload"));
        services.Configure<DataImportFileRetentionOptions>(configuration.GetSection("DataImport:FileRetention"));
        services.AddScoped<IImportFileValidator, ImportFileValidator>();
        AddImportFileStorage(services, configuration, environment);

        services.AddScoped<UploadImportFileHandler>();
        services.AddScoped<IValidator<UploadImportFileRequest>, UploadImportFileValidator>();

        services.AddScoped<EmployeeImportFileParser>();
        services.AddScoped<EmployeeStagingRowValidator>();
        services.AddScoped<ValidateImportSessionHandler>();
        services.AddScoped<IValidator<ValidateImportSessionRequest>, ValidateImportSessionValidator>();

        services.AddScoped<GetImportSessionColumnsHandler>();
        services.AddScoped<IValidator<GetImportSessionColumnsRequest>, GetImportSessionColumnsValidator>();

        services.AddScoped<GetImportPreviewHandler>();
        services.AddScoped<IValidator<GetImportPreviewRequest>, GetImportPreviewValidator>();

        services.AddScoped<ConfirmImportSessionHandler>();
        services.AddScoped<IValidator<ConfirmImportSessionRequest>, ConfirmImportSessionValidator>();

        services.AddScoped<ExportImportErrorsHandler>();
        services.AddScoped<IValidator<ExportImportErrorsRequest>, ExportImportErrorsValidator>();

        services.AddScoped<ListImportSessionsHandler>();
        services.AddScoped<IValidator<ListImportSessionsRequest>, ListImportSessionsValidator>();

        services.AddScoped<GetImportSessionHandler>();
        services.AddScoped<IValidator<GetImportSessionRequest>, GetImportSessionValidator>();

        services.AddScoped<DownloadImportTemplateHandler>();
        services.AddScoped<IValidator<DownloadImportTemplateRequest>, DownloadImportTemplateValidator>();

        services.AddScoped<IdempotencyMaintenanceJob>();
        services.AddScoped<PurgeImportSessionFilesJob>();
        services.AddScoped<PurgeOrphanedImportFileUploadsJob>();

        services.AddDbContext<DataImportDbContext>(options =>
            options.UseVersionedAggregates().UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "data_import")));

        return services;
    }

    private static void AddImportFileStorage(
        IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.TryAddSingleton(environment);

        var supabaseSection = configuration.GetSection("DataImport:Supabase:ImportFiles");

        if (supabaseSection.Exists() && !string.IsNullOrWhiteSpace(supabaseSection["SupabaseUrl"]))
        {
            services.AddOptions<Services.SupabaseImportFileStorageOptions>().Bind(supabaseSection).ValidateOnStart();
            services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<Services.SupabaseImportFileStorageOptions>, Services.SupabaseImportFileStorageOptionsValidator>();
            services.AddHttpClient<IImportFileStorageService, Services.SupabaseImportFileStorageService>();
            services.AddHealthChecks().AddCheck<Services.SupabaseImportFileStorageHealthCheck>(
                "import-file-storage", tags: ["degraded"]);
        }
        else if (IsLocalStorageAllowedEnvironment(environment))
        {
            services.AddScoped<IImportFileStorageService, LocalImportFileStorageService>();
        }
        else
        {
            throw new InvalidOperationException(
                "Import file storage is not configured for this environment. "
                + "'DataImport:Supabase:ImportFiles:SupabaseUrl' (and ServiceRoleKey/BucketName) must "
                + "be set in Staging/Production — the local temp-directory fallback is only permitted "
                + "in Development or an explicit automated-test environment.");
        }
    }

    private static bool IsLocalStorageAllowedEnvironment(IHostEnvironment environment) =>
        environment.IsDevelopment()
        || environment.IsEnvironment("Test")
        || string.Equals(Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase);

    public static WebApplication UseDataImportRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        // Ticket 3 (P1) follow-up item 4: clean up expired idempotency records.
        jobManager.AddOrUpdate<IdempotencyMaintenanceJob>(
            "dataimport-idempotency-maintenance",
            job => job.ExecuteAsync(),
            "*/5 * * * *");
        jobManager.AddOrUpdate<PurgeImportSessionFilesJob>(
            "dataimport-purge-session-files",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Hourly());
        jobManager.AddOrUpdate<PurgeOrphanedImportFileUploadsJob>(
            "dataimport-purge-orphaned-uploads",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Hourly());
        return app;
    }

    public static async Task MigrateDataImportAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataImportDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS data_import");
        await db.Database.MigrateAsync();
    }
}
