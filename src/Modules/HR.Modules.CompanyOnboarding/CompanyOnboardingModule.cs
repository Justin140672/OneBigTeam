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
    /// No seed data is needed for this module — a company's onboarding progress and task
    /// completions are lazily created the first time the checklist is viewed (see
    /// GetOnboardingChecklistHandler). Kept as a no-op for symmetry with other modules' startup
    /// wiring in HR.Api's Program.cs.
    /// </summary>
    public static Task SeedCompanyOnboardingAsync(this IServiceProvider services)
    {
        return Task.CompletedTask;
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
