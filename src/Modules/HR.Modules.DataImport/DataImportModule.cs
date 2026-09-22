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

        services.AddDbContext<DataImportDbContext>(options =>
            options.UseVersionedAggregates().UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "data_import")));

        return services;
    }

    /// <summary>
    /// Reliability review issue 2 (P1): import files must be durable (Supabase-backed) in every
    /// environment except Development or an explicit automated-test environment, matching the rule
    /// already applied to documents/profile photos/support attachments/organisation exports/candidate
    /// documents. This matters more here than for most other categories: import validation and
    /// confirmation both happen in requests after the initial upload, so a process restart between
    /// stages on local temp storage would orphan an otherwise-valid import — Supabase-backed storage
    /// keeps the file available from any service instance across the whole import lifecycle.
    /// </summary>
    private static void AddImportFileStorage(
        IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var supabaseSection = configuration.GetSection("DataImport:Supabase:ImportFiles");

        if (supabaseSection.Exists() && !string.IsNullOrWhiteSpace(supabaseSection["SupabaseUrl"]))
        {
            services.Configure<Services.SupabaseImportFileStorageOptions>(supabaseSection);
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
