using FluentValidation;
using HR.Modules.Companies.Features.CreateCompany;
using HR.Modules.Companies.Features.GetCompany;
using HR.Modules.Companies.Features.UpdateCompany;
using HR.Modules.Companies.Features.UpdateCompanySettings;
using HR.Modules.Companies.Features.UploadCompanyLogo;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Companies;

public static class CompaniesModule
{
    public static IServiceCollection AddCompaniesModule(
        this IServiceCollection services,
        string connectionString)
    {
        AddFeatureServices(services);

        services.AddDbContext<CompaniesDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "companies")));

        return services;
    }

    public static async Task MigrateCompaniesAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        await db.Database.MigrateAsync();
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<CreateCompanyHandler>();
        services.AddScoped<GetCompanyHandler>();
        services.AddScoped<UpdateCompanyHandler>();
        services.AddScoped<UpdateCompanySettingsHandler>();
        services.AddScoped<UploadCompanyLogoHandler>();
        services.AddScoped<IBrandingStorage, StubBrandingStorage>();
        services.AddScoped<ICompanyAuditEventPublisher, LoggerCompanyAuditEventPublisher>();
        services.AddScoped<IValidator<CreateCompanyRequest>, CreateCompanyValidator>();
        services.AddScoped<IValidator<UpdateCompanyRequest>, UpdateCompanyValidator>();
        services.AddScoped<IValidator<UpdateCompanySettingsRequest>, UpdateCompanySettingsValidator>();
        services.AddScoped<IValidator<UploadCompanyLogoRequest>, UploadCompanyLogoValidator>();
    }
}
