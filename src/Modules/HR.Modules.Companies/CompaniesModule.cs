using FluentValidation;

using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.CancelSubscription;
using HR.Modules.Companies.Features.CreateBillingPortalSession;
using HR.Modules.Companies.Features.CreateCheckoutSession;
using HR.Modules.Companies.Features.CreateCompany;
using HR.Modules.Companies.Features.CreatePublicHoliday;
using HR.Modules.Companies.Features.GetCompany;
using HR.Modules.Companies.Features.GetCompanySettings;
using HR.Modules.Companies.Features.GetHrSettings;
using HR.Modules.Companies.Features.GetSubscriptionDetails;
using HR.Modules.Companies.Features.GetSubscriptionStatus;
using HR.Modules.Companies.Features.ListPublicHolidays;
using HR.Modules.Companies.Features.ResumeSubscription;
using HR.Modules.Companies.Features.StripeWebhook;
using HR.Modules.Companies.Features.UpdateCompany;
using HR.Modules.Companies.Features.UpdateCompanySettings;
using HR.Modules.Companies.Features.UpdateHrSettings;
using HR.Modules.Companies.Features.UpdatePublicHoliday;
using HR.Modules.Companies.Features.UploadCompanyLogo;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Companies.Services.OnboardingTasks;
using HR.Modules.Companies.Storage;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Companies;

public static class CompaniesModule
{
    /// <summary>
    /// Registers Phase D's ReadOnlyModeMiddleware. Must be called after UseIdentityModule (and
    /// UseAuthentication) so the current tenant is already resolvable via ICurrentTenant.
    /// </summary>
    public static IApplicationBuilder UseCompaniesModule(this IApplicationBuilder app)
    {
        app.UseMiddleware<ReadOnlyModeMiddleware>();
        return app;
    }

    public static IServiceCollection AddCompaniesModule(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        AddFeatureServices(services);

        services.Configure<StripeOptions>(configuration.GetSection("Stripe"));

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
        var acme = await db.Companies.SingleOrDefaultAsync(c => c.Id == acmeId);
        if (acme is null)
        {
            acme = Company.Create(acmeId, "Acme Corporation", now);
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
        var betaCorp = await db.Companies.SingleOrDefaultAsync(c => c.Id == betaCorpId);
        if (betaCorp is null)
        {
            betaCorp = Company.Create(betaCorpId, "Beta Corp", now);
            betaCorp.SetAddress(
                CompanyAddress.Create(Guid.NewGuid(), betaCorpId, CompanyAddressType.RegisteredOffice,
                    "10 Innovation Drive", null, "Bristol", null, "BS1 1AA", "GB", now),
                now);
            db.Companies.Add(betaCorp);
        }

        // Both seeded dev/E2E companies must have a persisted company_settings row, same as every
        // real company gets from CompanyProvisioner.ProvisionCompanyAsync on signup — without one,
        // EmployeeNumberGenerator.GenerateNextAsync (Automatic mode's atomic next-number counter)
        // has no row to claim/increment against and throws. CreateDefault() only supplies the same
        // Automatic-mode defaults a real company already gets; it does not change either company's
        // numbering mode. Guarded by Settings being null so this never overwrites a mode an admin
        // has since changed via HR Settings.
        if (acme.Settings is null)
        {
            acme.SetSettings(CompanySettings.CreateDefault(acmeId, now), now);
        }
        if (betaCorp.Settings is null)
        {
            betaCorp.SetSettings(CompanySettings.CreateDefault(betaCorpId, now), now);
        }

        await db.SaveChangesAsync();

        // Seeded dev companies get an already-active subscription (rather than a trial) so dev
        // personas are never read-only-gated — real trials only start via the self-service SignUp
        // flow (Identity's SignUp feature, via ICompanyProvisioner).
        foreach (var seededCompanyId in new[] { acmeId, betaCorpId })
        {
            if (!await db.CustomerSubscriptions.AnyAsync(s => s.CompanyId == seededCompanyId))
            {
                var subscription = CustomerSubscription.StartTrial(seededCompanyId, now, trialLengthDays: 14);
                subscription.ActivateSubscription(
                    stripeCustomerId: "dev-stub-customer",
                    stripeSubscriptionId: "dev-stub-subscription",
                    priceId: "dev-stub-price",
                    currentPeriodEnd: now.AddYears(1),
                    now);
                db.CustomerSubscriptions.Add(subscription);
            }
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
        services.AddScoped<GetHrSettingsHandler>();
        services.AddScoped<GetSubscriptionStatusHandler>();
        services.AddScoped<UpdateCompanyHandler>();
        services.AddScoped<UpdateCompanySettingsHandler>();
        services.AddScoped<UpdateHrSettingsHandler>();
        services.AddScoped<UploadCompanyLogoHandler>();
        services.AddScoped<IBrandingStorage, StubBrandingStorage>();
        services.AddScoped<ICompanyLeaveSettingsReader, CompanyLeaveSettingsReader>();
        services.AddScoped<ICompanyProbationSettingsReader, CompanyProbationSettingsReader>();
        services.AddScoped<ICompanyNoticePeriodSettingsReader, CompanyNoticePeriodSettingsReader>();
        services.AddScoped<ICompanyLeavingSettingsReader, CompanyLeavingSettingsReader>();
        services.AddScoped<ICompanySicknessSettingsReader, CompanySicknessSettingsReader>();
        services.AddScoped<ICompanyAcknowledgementSettingsReader, CompanyAcknowledgementSettingsReader>();
        services.AddScoped<ICompanyContactValidationReader, CompanyContactValidationReader>();
        services.AddScoped<ICompanyTimeZoneReader, CompanyTimeZoneReader>();
        services.AddScoped<ICompanyEmployeeNumberSettingsReader, CompanyEmployeeNumberSettingsReader>();
        services.AddScoped<IEmployeeNumberGenerator, EmployeeNumberGenerator>();
        services.AddScoped<IPublicHolidayReader, PublicHolidayReader>();
        services.AddScoped<ISubscriptionStatusReader, SubscriptionStatusReader>();
        services.AddScoped<ICompanyProvisioner, CompanyProvisioner>();
        services.AddScoped<CreatePublicHolidayHandler>();
        services.AddScoped<IValidator<CreatePublicHolidayRequest>, CreatePublicHolidayValidator>();
        services.AddScoped<ListPublicHolidaysHandler>();
        services.AddScoped<UpdatePublicHolidayHandler>();
        services.AddScoped<IValidator<UpdatePublicHolidayRequest>, UpdatePublicHolidayValidator>();
        services.AddScoped<IValidator<CreateCompanyRequest>, CreateCompanyValidator>();
        services.AddScoped<IValidator<UpdateCompanyRequest>, UpdateCompanyValidator>();
        services.AddScoped<IValidator<UpdateCompanySettingsRequest>, UpdateCompanySettingsValidator>();
        services.AddScoped<IValidator<UpdateHrSettingsRequest>, UpdateHrSettingsValidator>();
        services.AddScoped<IValidator<UploadCompanyLogoRequest>, UploadCompanyLogoValidator>();

        // Getting Started checklist task definitions (HR.Modules.CompanyOnboarding epic, Phase A) —
        // multi-registered against the shared IOnboardingTaskDefinition contract so
        // CompanyOnboarding's OnboardingTaskRegistry can aggregate them without referencing
        // HR.Modules.Companies directly.
        services.AddScoped<IOnboardingTaskDefinition, CompleteCompanyDetailsTask>();
        services.AddScoped<IOnboardingTaskDefinition, ConfigureHrSettingsTask>();

        // Phase B (trial/subscription tracking) — optional checklist item, excluded from the
        // mandatory completion percentage.
        services.AddScoped<IOnboardingTaskDefinition, StartSubscriptionTask>();

        // Phase C — Stripe checkout + webhook.
        services.AddScoped<IStripeGateway, StripeGateway>();
        services.AddScoped<CreateCheckoutSessionHandler>();
        services.AddScoped<StripeWebhookHandler>();

        // Phase D — subscription management (details, cancel/resume, billing portal).
        services.AddScoped<GetSubscriptionDetailsHandler>();
        services.AddScoped<CancelSubscriptionHandler>();
        services.AddScoped<ResumeSubscriptionHandler>();
        services.AddScoped<CreateBillingPortalSessionHandler>();
    }
}
