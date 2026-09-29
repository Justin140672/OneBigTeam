using Hangfire;
using HR.SharedKernel;
using HR.Modules.CompanyOnboarding.Features.DismissOnboardingChecklist;
using HR.Modules.CompanyOnboarding.Features.GetOnboardingChecklist;
using HR.Modules.CompanyOnboarding.Features.MarkOnboardingTaskComplete;
using HR.Modules.CompanyOnboarding.Jobs;
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
            options.UseVersionedAggregates().UseNpgsql(connectionString, npgsql =>
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

    public static async Task SeedCompanyOnboardingAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanyOnboardingDbContext>();

        var now = DateTimeOffset.UtcNow;
        var seededCompanyIds = new[]
        {
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
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

    public static IApplicationBuilder UseCompanyOnboardingModule(this IApplicationBuilder app)
    {
        return app;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<OnboardingTaskRegistry>();

        services.AddScoped<GetOnboardingChecklistHandler>();
        services.AddScoped<DismissOnboardingChecklistHandler>();
        services.AddScoped<MarkOnboardingTaskCompleteHandler>();

        services.AddScoped<IdempotencyMaintenanceJob>();
    }

    public static WebApplication UseCompanyOnboardingRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        // Ticket 3 (P1) follow-up item 4: clean up expired idempotency records.
        jobManager.AddOrUpdate<IdempotencyMaintenanceJob>(
            "companyonboarding-idempotency-maintenance",
            job => job.ExecuteAsync(),
            "*/5 * * * *");
        return app;
    }
}
