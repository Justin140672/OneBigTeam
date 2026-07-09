using FluentValidation;
using HR.Modules.DataImport.Features.ConfirmImportSession;
using HR.Modules.DataImport.Features.GetImportPreview;
using HR.Modules.DataImport.Features.UploadImportFile;
using HR.Modules.DataImport.Features.ValidateImportSession;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.DataImport;

public static class DataImportModule
{
    public static IServiceCollection AddDataImportModule(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        services.Configure<ImportFileUploadOptions>(configuration.GetSection("DataImport:FileUpload"));
        services.AddScoped<IImportFileValidator, ImportFileValidator>();
        services.AddScoped<IImportFileStorageService, LocalImportFileStorageService>();

        services.AddScoped<UploadImportFileHandler>();
        services.AddScoped<IValidator<UploadImportFileRequest>, UploadImportFileValidator>();

        services.AddScoped<EmployeeImportFileParser>();
        services.AddScoped<EmployeeStagingRowValidator>();
        services.AddScoped<ValidateImportSessionHandler>();
        services.AddScoped<IValidator<ValidateImportSessionRequest>, ValidateImportSessionValidator>();

        services.AddScoped<GetImportPreviewHandler>();
        services.AddScoped<IValidator<GetImportPreviewRequest>, GetImportPreviewValidator>();

        services.AddScoped<ConfirmImportSessionHandler>();
        services.AddScoped<IValidator<ConfirmImportSessionRequest>, ConfirmImportSessionValidator>();

        services.AddDbContext<DataImportDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "data_import")));

        return services;
    }

    public static async Task MigrateDataImportAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataImportDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS data_import");
        await db.Database.MigrateAsync();
    }
}
