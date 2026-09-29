using FluentValidation;
using Hangfire;

using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.AdminCancelSubscription;
using HR.Modules.Companies.Features.CancelCustomerDeletion;
using HR.Modules.Companies.Features.CancelSubscription;
using HR.Modules.Companies.Features.CreateBillingPortalSession;
using HR.Modules.Companies.Features.CreateCheckoutSession;
using HR.Modules.Companies.Features.CreatePublicHoliday;
using HR.Modules.Companies.Features.ExecuteCustomerDeletion;
using HR.Modules.Companies.Features.ExtendCustomerTrial;
using HR.Modules.Companies.Features.ForceCustomerReadOnly;
using HR.Modules.Companies.Features.GetCompany;
using HR.Modules.Companies.Features.GetCompanySettings;
using HR.Modules.Companies.Features.GetCompanySettingsHistory;
using HR.Modules.Companies.Features.GetCompanyAuditLog;
using HR.Modules.Companies.Features.GetHrSettingsHistory;
using HR.Modules.Companies.Features.GetCustomerBillingBreakdown;
using HR.Modules.Companies.Features.GetCustomerBillingHistory;
using HR.Modules.Companies.Features.GetCustomerDashboard;
using HR.Modules.Companies.Features.GetCustomerDatabaseAssignment;
using HR.Modules.Companies.Features.GetCustomerDetails;
using HR.Modules.Companies.CustomerDatabase;
using HR.Modules.Companies.Features.GetDeletionQueue;
using HR.Modules.Companies.Features.PlaceCompanyLegalHold;
using HR.Modules.Companies.Features.LiftCompanyLegalHold;
using HR.Modules.Companies.Features.GetEmployeeRenumberSideEffectStatus;
using HR.Modules.Companies.Features.RetryEmployeeRenumberSideEffect;
using HR.Modules.Companies.Features.GetCustomerSupportView;
using HR.Modules.Companies.Features.GetFailedPayments;
using HR.Modules.Companies.Features.GetHrSettings;
using HR.Modules.Companies.Features.GetSubscriptionDetails;
using HR.Modules.Companies.Features.GetSubscriptionStatus;
using HR.Modules.Companies.Features.GenerateSupportSession;
using HR.Modules.Companies.Features.ListBackgroundJobs;
using HR.Modules.Companies.Features.ListCustomers;
using HR.Modules.Companies.Features.ListPublicHolidays;
using HR.Modules.Companies.Features.RedeemSupportSession;
using HR.Modules.Companies.Features.ReinstateCustomerSubscription;
using HR.Modules.Companies.Features.RetryBackgroundJob;
using HR.Modules.Companies.Features.ResumeCustomerService;
using HR.Modules.Companies.Features.ScheduleCustomerDeletion;
using HR.Modules.Companies.Features.ResumeSubscription;
using HR.Modules.Companies.Features.RevokeSupportSession;
using HR.Modules.Companies.Features.SetCustomerOriginalStatus;
using HR.Modules.Companies.Features.StripeWebhook;
using HR.Modules.Companies.Features.UpdateCompany;
using HR.Modules.Companies.Features.UpdateCompanySettings;
using HR.Modules.Companies.Features.UpdateHrSettings;
using HR.Modules.Companies.Features.UpdatePublicHoliday;
using HR.Modules.Companies.Features.UpdateDocumentReminderSettings;
using HR.Modules.Companies.Jobs;
using HR.Modules.Companies.Features.UpdateNotificationSettings;
using HR.Modules.Companies.Features.UpdateRecruitmentSettings;
using HR.Modules.Companies.Features.UploadCompanyLogo;
using HR.Modules.Companies.Features.GetSystemHealth;
using HR.Modules.Companies.Features.GetApplicationMetrics;
using HR.Modules.Companies.Features.GetAuditLog;
using HR.Modules.Companies.Features.GetPlatformSettings;
using HR.Modules.Companies.Features.UpdatePlatformSettings;
using HR.Modules.Companies.Features.GetSubscriptionPricingConfig;
using HR.Modules.Companies.Features.UpdateSubscriptionPricingConfig;
using HR.Modules.Companies.Features.GetPublicSubscriptionPricing;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Companies.Services.OnboardingTasks;
using HR.Modules.Companies.Storage;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Companies;

public static class CompaniesModule
{
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
            options.UseVersionedAggregates().UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "companies")));

        services.AddDbContext<PlatformDbContext>(options =>
            options.UseVersionedAggregates().UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "platform")));

        services.AddHealthChecks()
            .AddCheck<CompaniesDatabaseHealthCheck>("database", tags: ["ready", "critical"])
            .AddCheck<StripeHealthCheck>("stripe", tags: ["degraded"]);

        return services;
    }

    public static async Task MigrateCompaniesAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS companies");
        await db.Database.MigrateAsync();
    }

    public static async Task MigratePlatformAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS platform");
        await db.Database.MigrateAsync();
    }

    public static async Task MigrateAndSeedCoreApplicationAsync(this IServiceProvider services)
    {
        await services.MigratePlatformAsync();

        await services.MigrateCompaniesAsync();

        await services.SeedCompaniesAsync();
    }

    public static async Task SeedCompaniesAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var now = DateTimeOffset.UtcNow;

        var acmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var acme = await db.Companies.Include(c => c.Settings).SingleOrDefaultAsync(c => c.Id == acmeId);
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
        var betaCorp = await db.Companies.Include(c => c.Settings).SingleOrDefaultAsync(c => c.Id == betaCorpId);
        if (betaCorp is null)
        {
            betaCorp = Company.Create(betaCorpId, "Beta Corp", now);
            betaCorp.SetAddress(
                CompanyAddress.Create(Guid.NewGuid(), betaCorpId, CompanyAddressType.RegisteredOffice,
                    "10 Innovation Drive", null, "Bristol", null, "BS1 1AA", "GB", now),
                now);
            db.Companies.Add(betaCorp);
        }

        var gammaId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var gamma = await db.Companies.Include(c => c.Settings).SingleOrDefaultAsync(c => c.Id == gammaId);
        if (gamma is null)
        {
            gamma = Company.Create(gammaId, "Gamma Industries", now);
            gamma.SetAddress(
                CompanyAddress.Create(Guid.NewGuid(), gammaId, CompanyAddressType.RegisteredOffice,
                    "22 Gamma Way", null, "Leeds", null, "LS1 1AA", "GB", now),
                now);
            db.Companies.Add(gamma);
        }

        if (acme.Settings is null)
        {
            acme.SetSettings(CompanySettings.CreateDefault(acmeId, now), now);
        }
        if (betaCorp.Settings is null)
        {
            betaCorp.SetSettings(CompanySettings.CreateDefault(betaCorpId, now), now);
        }
        if (gamma.Settings is null)
        {
            gamma.SetSettings(CompanySettings.CreateDefault(gammaId, now), now);
        }

        await db.SaveChangesAsync();

        if (!await db.CustomerSubscriptions.AnyAsync(s => s.CompanyId == acmeId))
        {
            var acmeSubscription = CustomerSubscription.StartTrial(acmeId, now, trialLengthDays: 14);
            db.CustomerSubscriptions.Add(acmeSubscription);
        }

        if (!await db.CustomerSubscriptions.AnyAsync(s => s.CompanyId == betaCorpId))
        {
            var betaSubscription = CustomerSubscription.StartTrial(betaCorpId, now, trialLengthDays: 14);
            betaSubscription.ActivateSubscription(
                stripeCustomerId: "dev-stub-customer",
                stripeSubscriptionId: "dev-stub-subscription",
                priceId: "dev-stub-price",
                currentPeriodEnd: now.AddYears(1),
                now);
            db.CustomerSubscriptions.Add(betaSubscription);
        }

        if (!await db.CustomerSubscriptions.AnyAsync(s => s.CompanyId == gammaId))
        {
            var gammaSubscription = CustomerSubscription.StartTrial(gammaId, now, trialLengthDays: 14);
            gammaSubscription.ActivateSubscription(
                stripeCustomerId: "dev-stub-customer-gamma",
                stripeSubscriptionId: "dev-stub-subscription-gamma",
                priceId: "dev-stub-price",
                currentPeriodEnd: now.AddYears(1),
                now);
            db.CustomerSubscriptions.Add(gammaSubscription);
        }

        await db.SaveChangesAsync();

        if (!await db.PublicHolidays.AnyAsync())
        {
            db.PublicHolidays.AddRange(
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000101"), acmeId, new DateOnly(2025,  1,  1), "New Year's Day",          "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000102"), acmeId, new DateOnly(2025,  4, 18), "Good Friday",             "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000103"), acmeId, new DateOnly(2025,  4, 21), "Easter Monday",           "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000104"), acmeId, new DateOnly(2025,  5,  5), "Early May Bank Holiday",  "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000105"), acmeId, new DateOnly(2025,  5, 26), "Spring Bank Holiday",     "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000106"), acmeId, new DateOnly(2025,  8, 25), "Summer Bank Holiday",     "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000107"), acmeId, new DateOnly(2025, 12, 25), "Christmas Day",           "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000108"), acmeId, new DateOnly(2025, 12, 26), "Boxing Day",              "GB", now),

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
        services.AddScoped<GetCompanyHandler>();
        services.AddScoped<GetCompanySettingsHandler>();
        services.AddScoped<GetHrSettingsHandler>();
        services.AddScoped<GetSubscriptionStatusHandler>();
        services.AddScoped<GetCustomerDashboardHandler>();
        services.AddScoped<UpdateCompanyHandler>();
        services.AddScoped<UpdateCompanySettingsHandler>();
        services.AddScoped<UpdateHrSettingsHandler>();
        services.AddScoped<EmployeeRenumberSideEffectJob>();
        services.AddScoped<GetEmployeeRenumberSideEffectStatusHandler>();
        services.AddScoped<IValidator<GetEmployeeRenumberSideEffectStatusRequest>, GetEmployeeRenumberSideEffectStatusValidator>();
        services.AddScoped<RetryEmployeeRenumberSideEffectHandler>();
        services.AddScoped<IValidator<RetryEmployeeRenumberSideEffectRequest>, RetryEmployeeRenumberSideEffectValidator>();
        services.AddScoped<UploadCompanyLogoHandler>();
        services.AddScoped<IBrandingStorage, StubBrandingStorage>();
        services.AddScoped<ICompanyLeaveSettingsReader, CompanyLeaveSettingsReader>();
        services.AddScoped<ICompanyProbationSettingsReader, CompanyProbationSettingsReader>();
        services.AddScoped<ICompanyNoticePeriodSettingsReader, CompanyNoticePeriodSettingsReader>();
        services.AddScoped<ICompanyLeavingSettingsReader, CompanyLeavingSettingsReader>();
        services.AddScoped<ICompanySicknessSettingsReader, CompanySicknessSettingsReader>();
        services.AddScoped<ICompanyRecruitmentSettingsReader, CompanyRecruitmentSettingsReader>();
        services.AddScoped<ICompanyNotificationSettingsReader, CompanyNotificationSettingsReader>();
        services.AddScoped<ICompanyDocumentReminderSettingsReader, CompanyDocumentReminderSettingsReader>();
        services.AddScoped<ICompanyAcknowledgementSettingsReader, CompanyAcknowledgementSettingsReader>();
        services.AddScoped<ICompanyContactValidationReader, CompanyContactValidationReader>();
        services.AddScoped<ICompanyTimeZoneReader, CompanyTimeZoneReader>();
        services.AddScoped<ICompanyEmployeeNumberSettingsReader, CompanyEmployeeNumberSettingsReader>();
        services.AddScoped<ICompanyWorkingPatternSettingsReader, CompanyWorkingPatternSettingsReader>();
        services.AddScoped<IEmployeeNumberGenerator, EmployeeNumberGenerator>();
        services.AddScoped<ICompanyAssetNumberSettingsReader, CompanyAssetNumberSettingsReader>();
        services.AddScoped<IAssetNumberGenerator, AssetNumberGenerator>();
        services.AddScoped<IActiveCompanyDirectory, ActiveCompanyDirectory>();
        services.AddScoped<IPublicHolidayReader, PublicHolidayReader>();
        services.AddScoped<ISubscriptionStatusReader, SubscriptionStatusReader>();
        services.AddScoped<ICompanyProvisioner, CompanyProvisioner>();
        services.AddScoped<CreatePublicHolidayHandler>();
        services.AddScoped<IValidator<CreatePublicHolidayRequest>, CreatePublicHolidayValidator>();
        services.AddScoped<ListPublicHolidaysHandler>();
        services.AddScoped<UpdatePublicHolidayHandler>();
        services.AddScoped<IValidator<UpdatePublicHolidayRequest>, UpdatePublicHolidayValidator>();
        services.AddScoped<IValidator<UpdateCompanyRequest>, UpdateCompanyValidator>();
        services.AddScoped<IValidator<UpdateCompanySettingsRequest>, UpdateCompanySettingsValidator>();
        services.AddScoped<IValidator<UpdateHrSettingsRequest>, UpdateHrSettingsValidator>();
        services.AddScoped<UpdateRecruitmentSettingsHandler>();
        services.AddScoped<IValidator<UpdateRecruitmentSettingsRequest>, UpdateRecruitmentSettingsValidator>();
        services.AddScoped<UpdateNotificationSettingsHandler>();
        services.AddScoped<IValidator<UpdateNotificationSettingsRequest>, UpdateNotificationSettingsValidator>();
        services.AddScoped<UpdateDocumentReminderSettingsHandler>();
        services.AddScoped<IValidator<UpdateDocumentReminderSettingsRequest>, UpdateDocumentReminderSettingsValidator>();
        services.AddScoped<GetCompanySettingsHistoryHandler>();
        services.AddScoped<IValidator<GetCompanySettingsHistoryRequest>, GetCompanySettingsHistoryValidator>();
        services.AddScoped<GetHrSettingsHistoryHandler>();
        services.AddScoped<IValidator<GetHrSettingsHistoryRequest>, GetHrSettingsHistoryValidator>();
        services.AddScoped<GetCompanyAuditLogHandler>();
        services.AddScoped<IValidator<GetCompanyAuditLogRequest>, GetCompanyAuditLogValidator>();
        services.AddScoped<IValidator<UploadCompanyLogoRequest>, UploadCompanyLogoValidator>();

        services.AddScoped<IOnboardingTaskDefinition, CompleteCompanyDetailsTask>();
        services.AddScoped<IOnboardingTaskDefinition, ConfigureHrSettingsTask>();

        services.AddScoped<IOnboardingTaskDefinition, StartSubscriptionTask>();

        var isE2ETestingForStripe = string.Equals(
            Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase);
        if (isE2ETestingForStripe)
        {
            services.AddScoped<IStripeGateway, E2eStripeGateway>();
        }
        else
        {
            services.AddScoped<IStripeGateway, StripeGateway>();
        }
        services.AddScoped<CreateCheckoutSessionHandler>();
        services.AddScoped<StripeWebhookHandler>();

        services.AddScoped<GetSubscriptionDetailsHandler>();
        services.AddScoped<CancelSubscriptionHandler>();
        services.AddScoped<ResumeSubscriptionHandler>();
        services.AddScoped<CreateBillingPortalSessionHandler>();

        services.AddScoped<ListCustomersHandler>();
        services.AddScoped<IValidator<ListCustomersRequest>, ListCustomersValidator>();

        services.AddScoped<GetCustomerDetailsHandler>();

        services.AddScoped<GetCustomerBillingBreakdownHandler>();
        services.AddScoped<IValidator<GetCustomerBillingBreakdownRequest>, GetCustomerBillingBreakdownValidator>();

        services.AddScoped<GetCustomerBillingHistoryHandler>();
        services.AddScoped<IValidator<GetCustomerBillingHistoryRequest>, GetCustomerBillingHistoryValidator>();

        services.AddScoped<GetFailedPaymentsHandler>();
        services.AddScoped<IValidator<GetFailedPaymentsRequest>, GetFailedPaymentsValidator>();

        services.AddScoped<GetCustomerSupportViewHandler>();
        services.AddScoped<IValidator<GetCustomerSupportViewRequest>, GetCustomerSupportViewValidator>();

        services.AddScoped<ExtendCustomerTrialHandler>();
        services.AddScoped<IValidator<ExtendCustomerTrialRequest>, ExtendCustomerTrialValidator>();
        services.AddScoped<AdminCancelSubscriptionHandler>();
        services.AddScoped<IValidator<AdminCancelSubscriptionRequest>, AdminCancelSubscriptionValidator>();
        services.AddScoped<ReinstateCustomerSubscriptionHandler>();
        services.AddScoped<IValidator<ReinstateCustomerSubscriptionRequest>, ReinstateCustomerSubscriptionValidator>();
        services.AddScoped<ForceCustomerReadOnlyHandler>();
        services.AddScoped<IValidator<ForceCustomerReadOnlyRequest>, ForceCustomerReadOnlyValidator>();
        services.AddScoped<ResumeCustomerServiceHandler>();
        services.AddScoped<IValidator<ResumeCustomerServiceRequest>, ResumeCustomerServiceValidator>();

        // Ticket 2: customer classification (original vs. new) — filters product update communications.
        services.AddScoped<SetCustomerOriginalStatusHandler>();
        services.AddScoped<IValidator<SetCustomerOriginalStatusRequest>, SetCustomerOriginalStatusValidator>();

        services.AddScoped<ScheduleCustomerDeletionHandler>();
        services.AddScoped<IValidator<ScheduleCustomerDeletionRequest>, ScheduleCustomerDeletionValidator>();
        services.AddScoped<CancelCustomerDeletionHandler>();
        services.AddScoped<IValidator<CancelCustomerDeletionRequest>, CancelCustomerDeletionValidator>();
        services.AddScoped<ExecuteCustomerDeletionHandler>();
        services.AddScoped<IValidator<ExecuteCustomerDeletionRequest>, ExecuteCustomerDeletionValidator>();
        services.AddScoped<GetDeletionQueueHandler>();

        services.AddScoped<PlaceCompanyLegalHoldHandler>();
        services.AddScoped<IValidator<PlaceCompanyLegalHoldRequest>, PlaceCompanyLegalHoldValidator>();
        services.AddScoped<LiftCompanyLegalHoldHandler>();
        services.AddScoped<IValidator<LiftCompanyLegalHoldRequest>, LiftCompanyLegalHoldValidator>();
        services.AddScoped<ILegalHoldStatusReader, LegalHoldStatusReader>();

        services.AddScoped<GenerateSupportSessionHandler>();
        services.AddScoped<IValidator<GenerateSupportSessionRequest>, GenerateSupportSessionValidator>();
        services.AddScoped<RevokeSupportSessionHandler>();
        services.AddScoped<IValidator<RevokeSupportSessionRequest>, RevokeSupportSessionValidator>();
        services.AddScoped<RedeemSupportSessionHandler>();
        services.AddScoped<IValidator<RedeemSupportSessionRequest>, RedeemSupportSessionValidator>();

        services.AddScoped<ListBackgroundJobsHandler>();
        services.AddScoped<RetryBackgroundJobHandler>();
        services.AddScoped<IValidator<RetryBackgroundJobRequest>, RetryBackgroundJobValidator>();

        services.AddScoped<GetSystemHealthHandler>();

        services.AddScoped<GetApplicationMetricsHandler>();

        services.AddScoped<GetAuditLogHandler>();
        services.AddScoped<IValidator<GetAuditLogRequest>, GetAuditLogValidator>();

        // Ticket 1 Phase 3 — Customer Database Assignment (dedicated per-customer database assignment
        // tracking). Platform-admin endpoint for viewing a customer's database assignment status.
        services.AddScoped<GetCustomerDatabaseAssignmentHandler>();
        services.AddScoped<CustomerDatabaseConnection>();

        services.AddScoped<GetPlatformSettingsHandler>();
        services.AddScoped<IValidator<GetPlatformSettingsRequest>, GetPlatformSettingsValidator>();
        services.AddScoped<UpdatePlatformSettingsHandler>();
        services.AddScoped<IValidator<UpdatePlatformSettingsRequest>, UpdatePlatformSettingsValidator>();

        services.AddScoped<GetSubscriptionPricingConfigHandler>();
        services.AddScoped<IValidator<GetSubscriptionPricingConfigRequest>, GetSubscriptionPricingConfigValidator>();
        services.AddScoped<UpdateSubscriptionPricingConfigHandler>();
        services.AddScoped<IValidator<UpdateSubscriptionPricingConfigRequest>, UpdateSubscriptionPricingConfigValidator>();
        services.AddScoped<GetPublicSubscriptionPricingHandler>();
        services.AddScoped<IValidator<GetPublicSubscriptionPricingRequest>, GetPublicSubscriptionPricingValidator>();

        services.AddScoped<Jobs.IdempotencyMaintenanceJob>();
    }

    public static WebApplication UseCompaniesRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        // Ticket 3 (P1) follow-up item 4: clean up expired idempotency records.
        jobManager.AddOrUpdate<Jobs.IdempotencyMaintenanceJob>(
            "companies-idempotency-maintenance",
            job => job.ExecuteAsync(),
            "*/5 * * * *");
        return app;
    }
}
