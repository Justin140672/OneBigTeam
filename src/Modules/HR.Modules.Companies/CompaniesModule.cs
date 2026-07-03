using FluentValidation;

using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.CreateCompany;
using HR.Modules.Companies.Features.CreatePublicHoliday;
using HR.Modules.Companies.Features.GetCompany;
using HR.Modules.Companies.Features.GetCompanySettings;
using HR.Modules.Companies.Features.ListPublicHolidays;
using HR.Modules.Companies.Features.UpdateCompany;
using HR.Modules.Companies.Features.UpdateCompanySettings;
using HR.Modules.Companies.Features.UpdatePublicHoliday;
using HR.Modules.Companies.Features.UploadCompanyLogo;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Companies.Storage;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

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
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS companies");
        await db.Database.MigrateAsync();
    }

    public static async Task SeedCompaniesAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var now = DateTimeOffset.UtcNow;

        var acmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        if (!await db.Companies.AnyAsync(c => c.Id == acmeId))
        {
            var acme = Company.Create(acmeId, "Acme Corporation", now);
            acme.SetAddress(
                CompanyAddress.Create(Guid.NewGuid(), acmeId, CompanyAddressType.RegisteredOffice,
                    "123 Main Street", null, "London", null, "EC1A 1BB", "GB", now),
                now);
            acme.SetAddress(
                CompanyAddress.Create(Guid.NewGuid(), acmeId, CompanyAddressType.TradingAddress,
                    "456 High Street", "Floor 2", "Manchester", null, "M1 1AE", "GB", now),
                now);
            db.Companies.Add(acme);
        }

        var betaCorpId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        if (!await db.Companies.AnyAsync(c => c.Id == betaCorpId))
        {
            var betaCorp = Company.Create(betaCorpId, "Beta Corp", now);
            betaCorp.SetAddress(
                CompanyAddress.Create(Guid.NewGuid(), betaCorpId, CompanyAddressType.RegisteredOffice,
                    "10 Innovation Drive", null, "Bristol", null, "BS1 1AA", "GB", now),
                now);
            db.Companies.Add(betaCorp);
        }

        await db.SaveChangesAsync();

        if (!await db.PublicHolidays.AnyAsync())
        {
            db.PublicHolidays.AddRange(
                // 2025 — England & Wales
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000101"), acmeId, new DateOnly(2025,  1,  1), "New Year's Day",          "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000102"), acmeId, new DateOnly(2025,  4, 18), "Good Friday",             "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000103"), acmeId, new DateOnly(2025,  4, 21), "Easter Monday",           "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000104"), acmeId, new DateOnly(2025,  5,  5), "Early May Bank Holiday",  "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000105"), acmeId, new DateOnly(2025,  5, 26), "Spring Bank Holiday",     "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000106"), acmeId, new DateOnly(2025,  8, 25), "Summer Bank Holiday",     "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000107"), acmeId, new DateOnly(2025, 12, 25), "Christmas Day",           "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000108"), acmeId, new DateOnly(2025, 12, 26), "Boxing Day",              "GB", now),

                // 2026 — England & Wales
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000201"), acmeId, new DateOnly(2026,  1,  1), "New Year's Day",          "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000202"), acmeId, new DateOnly(2026,  4,  3), "Good Friday",             "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000203"), acmeId, new DateOnly(2026,  4,  6), "Easter Monday",           "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000204"), acmeId, new DateOnly(2026,  5,  4), "Early May Bank Holiday",  "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000205"), acmeId, new DateOnly(2026,  5, 25), "Spring Bank Holiday",     "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000206"), acmeId, new DateOnly(2026,  8, 31), "Summer Bank Holiday",     "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000207"), acmeId, new DateOnly(2026, 12, 25), "Christmas Day",           "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000208"), acmeId, new DateOnly(2026, 12, 28), "Boxing Day (substitute)", "GB", now));

            await db.SaveChangesAsync();
        }
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<CreateCompanyHandler>();
        services.AddScoped<GetCompanyHandler>();
        services.AddScoped<GetCompanySettingsHandler>();
        services.AddScoped<UpdateCompanyHandler>();
        services.AddScoped<UpdateCompanySettingsHandler>();
        services.AddScoped<UploadCompanyLogoHandler>();
        services.AddScoped<IBrandingStorage, StubBrandingStorage>();
        services.AddScoped<ICompanyLeaveSettingsReader, CompanyLeaveSettingsReader>();
        services.AddScoped<ICompanyProbationSettingsReader, CompanyProbationSettingsReader>();
        services.AddScoped<ICompanySicknessSettingsReader, CompanySicknessSettingsReader>();
        services.AddScoped<IPublicHolidayReader, PublicHolidayReader>();
        services.AddScoped<CreatePublicHolidayHandler>();
        services.AddScoped<IValidator<CreatePublicHolidayRequest>, CreatePublicHolidayValidator>();
        services.AddScoped<ListPublicHolidaysHandler>();
        services.AddScoped<UpdatePublicHolidayHandler>();
        services.AddScoped<IValidator<UpdatePublicHolidayRequest>, UpdatePublicHolidayValidator>();
        services.AddScoped<IValidator<CreateCompanyRequest>, CreateCompanyValidator>();
        services.AddScoped<IValidator<UpdateCompanyRequest>, UpdateCompanyValidator>();
        services.AddScoped<IValidator<UpdateCompanySettingsRequest>, UpdateCompanySettingsValidator>();
        services.AddScoped<IValidator<UploadCompanyLogoRequest>, UploadCompanyLogoValidator>();
    }
}
