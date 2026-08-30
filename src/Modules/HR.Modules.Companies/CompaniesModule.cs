using FluentValidation;

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
using HR.Modules.Companies.Features.GetCustomerDetails;
using HR.Modules.Companies.Features.GetDeletionQueue;
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

        // System Health Dashboard (Platform Monitoring epic) — "database" proxies overall Postgres
        // connectivity (see CompaniesDatabaseHealthCheck remarks), "stripe" is a live account-balance
        // reachability probe.
        services.AddHealthChecks()
            .AddCheck<CompaniesDatabaseHealthCheck>("database")
            .AddCheck<StripeHealthCheck>("stripe");

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
        // Real Stripe gateway swapped for a no-op fake under E2E_TESTING — same rationale/pattern
        // as HR.Modules.Identity.IdentityModule's ISupabaseAuthGateway swap (see
        // E2eStripeGateway's own remarks for why this was needed: E2E-reachable code paths that
        // call Stripe were otherwise always hitting the real API, even against seeded companies'
        // fake "dev-stub-customer" Stripe customer ids).
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

        // Phase D — subscription management (details, cancel/resume, billing portal).
        services.AddScoped<GetSubscriptionDetailsHandler>();
        services.AddScoped<CancelSubscriptionHandler>();
        services.AddScoped<ResumeSubscriptionHandler>();
        services.AddScoped<CreateBillingPortalSessionHandler>();

        // Admin Portal customer list.
        services.AddScoped<ListCustomersHandler>();
        services.AddScoped<IValidator<ListCustomersRequest>, ListCustomersValidator>();

        // Admin Portal customer details.
        services.AddScoped<GetCustomerDetailsHandler>();

        // Admin Portal customer billing breakdown (persists a history snapshot on each view).
        services.AddScoped<GetCustomerBillingBreakdownHandler>();
        services.AddScoped<IValidator<GetCustomerBillingBreakdownRequest>, GetCustomerBillingBreakdownValidator>();

        // Admin Portal customer billing history (live Stripe invoice lookup, no local invoice data).
        services.AddScoped<GetCustomerBillingHistoryHandler>();
        services.AddScoped<IValidator<GetCustomerBillingHistoryRequest>, GetCustomerBillingHistoryValidator>();

        // Admin Portal Failed Payments Dashboard (Billing epic) — platform-wide, not scoped to a
        // single customer.
        services.AddScoped<GetFailedPaymentsHandler>();
        services.AddScoped<IValidator<GetFailedPaymentsRequest>, GetFailedPaymentsValidator>();

        // Admin Portal customer support view (Support epic) — condensed troubleshooting summary.
        services.AddScoped<GetCustomerSupportViewHandler>();
        services.AddScoped<IValidator<GetCustomerSupportViewRequest>, GetCustomerSupportViewValidator>();

        // Admin Portal subscription management (Subscription Management epic) — support
        // intervention actions for a platform administrator, each audited via IAuditEventPublisher.
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

        // Admin Portal Permanent Deletion Queue (Customer Lifecycle epic) — schedule/cancel/execute
        // support interventions, each audited via IAuditEventPublisher, plus the platform-wide
        // /deletion-queue list. "Execute" is a status-only, reversible-in-principle transition — see
        // ExecuteCustomerDeletionHandler's remarks for the explicit scope line (no real data
        // destruction here).
        services.AddScoped<ScheduleCustomerDeletionHandler>();
        services.AddScoped<IValidator<ScheduleCustomerDeletionRequest>, ScheduleCustomerDeletionValidator>();
        services.AddScoped<CancelCustomerDeletionHandler>();
        services.AddScoped<IValidator<CancelCustomerDeletionRequest>, CancelCustomerDeletionValidator>();
        services.AddScoped<ExecuteCustomerDeletionHandler>();
        services.AddScoped<IValidator<ExecuteCustomerDeletionRequest>, ExecuteCustomerDeletionValidator>();
        services.AddScoped<GetDeletionQueueHandler>();

        // Admin Portal "Login As Customer" support sessions (Support epic) — company-scoped,
        // time-boxed, single-use, revocable, audited access grants for platform administrators.
        services.AddScoped<GenerateSupportSessionHandler>();
        services.AddScoped<IValidator<GenerateSupportSessionRequest>, GenerateSupportSessionValidator>();
        services.AddScoped<RevokeSupportSessionHandler>();
        services.AddScoped<IValidator<RevokeSupportSessionRequest>, RevokeSupportSessionValidator>();
        services.AddScoped<RedeemSupportSessionHandler>();
        services.AddScoped<IValidator<RedeemSupportSessionRequest>, RedeemSupportSessionValidator>();

        // Admin Portal Job Monitoring (Background Jobs epic) — platform-wide, not scoped to a
        // single customer. Retry is audited via IAuditEventPublisher like the other admin actions.
        services.AddScoped<ListBackgroundJobsHandler>();
        services.AddScoped<RetryBackgroundJobHandler>();
        services.AddScoped<IValidator<RetryBackgroundJobRequest>, RetryBackgroundJobValidator>();

        // Admin Portal System Health Dashboard (Platform Monitoring epic) — platform-wide, not
        // scoped to a single customer. Aggregates the named health checks registered above and by
        // the other modules/Infrastructure via the framework's HealthCheckService rather than
        // re-implementing each check here.
        services.AddScoped<GetSystemHealthHandler>();

        // Admin Portal Application Metrics dashboard (Platform Monitoring epic) — platform-wide,
        // not scoped to a single customer.
        services.AddScoped<GetApplicationMetricsHandler>();

        // Admin Portal Platform Audit Log (Audit epic) — platform-wide, not scoped to a single
        // customer. Queries the existing cross-cutting IAuditHistoryReader/AuditDbContext rather
        // than a new audit table.
        services.AddScoped<GetAuditLogHandler>();
        services.AddScoped<IValidator<GetAuditLogRequest>, GetAuditLogValidator>();

        // Admin Portal Platform Settings (Platform Monitoring/Admin epic) — platform-wide singleton
        // row (trial length, default pricing display, support contact, maintenance mode, feature
        // flags), lazy-seeded on first read/write, each write audited via IAuditEventPublisher.
        services.AddScoped<GetPlatformSettingsHandler>();
        services.AddScoped<IValidator<GetPlatformSettingsRequest>, GetPlatformSettingsValidator>();
        services.AddScoped<UpdatePlatformSettingsHandler>();
        services.AddScoped<IValidator<UpdatePlatformSettingsRequest>, UpdatePlatformSettingsValidator>();
    }
}
