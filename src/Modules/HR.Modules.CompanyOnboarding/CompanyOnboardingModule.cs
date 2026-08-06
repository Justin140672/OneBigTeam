using HR.Modules.CompanyOnboarding.Features.DismissOnboardingChecklist;
using HR.Modules.CompanyOnboarding.Features.GetExploreCards;
using HR.Modules.CompanyOnboarding.Features.GetOnboardingChecklist;
using HR.Modules.CompanyOnboarding.Persistence;
using HR.Modules.CompanyOnboarding.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.CompanyOnboarding;

public static class CompanyOnboardingModule
{
    public static IServiceCollection AddCompanyOnboardingModule(
        this IServiceCollection services,
        string connectionString)
    {
        AddFeatureServices(services);

        services.AddDbContext<CompanyOnboardingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "company_onboarding")));

        return services;
    }

    public static async Task MigrateCompanyOnboardingAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanyOnboardingDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS company_onboarding");
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// Seeds the two long-lived dev/E2E companies (Acme, Beta Corp — see
    /// CompaniesModule.SeedCompaniesAsync) with an already-completed onboarding progress row, so
    /// AppSession.ShowGettingStarted is false and HR/Company Administrators land on their normal
    /// dashboard instead of "/getting-started" on every login. Without this, progress is lazily
    /// created the first time the checklist is viewed (see GetOnboardingChecklistHandler) with
    /// IsHidden=false — fine for a real brand-new company, but wrong for these shared, long-lived
    /// fixtures that dozens of other E2E tests assume land straight on a dashboard. The "/getting-
    /// started" page itself still has its own dedicated coverage (GettingStartedAndExploreTests),
    /// so this doesn't reduce what's tested — it just stops it leaking into unrelated tests.
    /// </summary>
    public static async Task SeedCompanyOnboardingAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanyOnboardingDbContext>();

        var now = DateTimeOffset.UtcNow;
        var seededCompanyIds = new[]
        {
            Guid.Parse("00000000-0000-0000-0000-000000000001"), // Acme Corporation
            Guid.Parse("00000000-0000-0000-0000-000000000002"), // Beta Corp
        };

        foreach (var companyId in seededCompanyIds)
        {
            if (await db.Progress.AnyAsync(p => p.CompanyId == companyId))
            {
                continue;
            }

            var progress = Domain.CompanyOnboardingProgress.Create(companyId, now);
            progress.MarkCompleted(now);
            db.Progress.Add(progress);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>No middleware is needed for this module in Phase A.</summary>
    public static IApplicationBuilder UseCompanyOnboardingModule(this IApplicationBuilder app)
    {
        return app;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<OnboardingTaskRegistry>();

        services.AddScoped<GetOnboardingChecklistHandler>();
        services.AddScoped<DismissOnboardingChecklistHandler>();
        services.AddScoped<GetExploreCardsHandler>();
    }
}
