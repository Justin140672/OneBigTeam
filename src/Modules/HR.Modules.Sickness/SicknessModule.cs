using Hangfire;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.CloseSicknessRecord;
using HR.Modules.Sickness.Features.CompleteReturnToWorkReviewFromTask;
using HR.Modules.Sickness.Features.FulfilEvidenceRequest;
using HR.Modules.Sickness.Features.CreateSicknessCategory;
using HR.Modules.Sickness.Features.DeactivateSicknessCategory;
using HR.Modules.Sickness.Features.GetSicknessRecord;
using HR.Modules.Sickness.Features.ListEmployeeSicknessRecords;
using HR.Modules.Sickness.Features.ListSicknessCategories;
using HR.Modules.Sickness.Features.RecordMySickness;
using HR.Modules.Sickness.Features.RecordSickness;
using HR.Modules.Sickness.Features.UpdateSicknessRecord;
using HR.Modules.Sickness.Features.UpdateSicknessCategory;
using HR.Modules.Sickness.Jobs;
using HR.Modules.Sickness.Persistence;
using HR.SharedKernel.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Sickness;

public static class SicknessModule
{
    public static IServiceCollection AddSicknessModule(
        this IServiceCollection services,
        string connectionString)
    {
        AddFeatureServices(services);

        services.AddDbContext<SicknessDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "sickness")));

        return services;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<ListSicknessCategoriesHandler>();
        services.AddScoped<CreateSicknessCategoryHandler>();
        services.AddScoped<UpdateSicknessCategoryHandler>();
        services.AddScoped<DeactivateSicknessCategoryHandler>();
        services.AddScoped<RecordSicknessHandler>();
        services.AddScoped<RecordMySicknessHandler>();
        services.AddScoped<GetSicknessRecordHandler>();
        services.AddScoped<ListEmployeeSicknessRecordsHandler>();
        services.AddScoped<UpdateSicknessRecordHandler>();
        services.AddScoped<CloseSicknessRecordHandler>();
        services.AddScoped<FitNoteRequestJob>();
        services.AddScoped<SicknessEvidenceReminderJob>();
        services.AddScoped<ITaskCompletionAction, SicknessEvidenceUploadCompletionAction>();
        services.AddScoped<ITaskCompletionAction, CompleteReturnToWorkReviewFromTaskAction>();
    }

    public static WebApplication UseSicknessRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        jobManager.AddOrUpdate<FitNoteRequestJob>(
            "fit-note-requests",
            job => job.ExecuteAsync(),
            Cron.Daily(3));
        jobManager.AddOrUpdate<SicknessEvidenceReminderJob>(
            "sickness-evidence-reminders",
            job => job.ExecuteAsync(),
            Cron.Daily(4));
        return app;
    }

    public static async Task MigrateSicknessAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SicknessDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS sickness");
        await db.Database.MigrateAsync();
    }

    public static async Task SeedSicknessAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SicknessDbContext>();

        if (await db.SicknessCategories.AnyAsync())
            return;

        var now    = DateTimeOffset.UtcNow;
        var acmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var coldFluId  = Guid.Parse("70000000-0000-0000-0000-000000000001");
        var backPainId = Guid.Parse("70000000-0000-0000-0000-000000000002");
        var migraineId = Guid.Parse("70000000-0000-0000-0000-000000000003");

        db.SicknessCategories.Add(SicknessCategory.Create(coldFluId,  acmeId, "Cold/Flu",  1, now));
        db.SicknessCategories.Add(SicknessCategory.Create(backPainId, acmeId, "Back Pain", 2, now));
        db.SicknessCategories.Add(SicknessCategory.Create(migraineId, acmeId, "Migraine",  3, now));

        // Sarah Chen (CTO) — a closed record and a currently-open one, so both the
        // Active/Closed status badge and the fit-note evidence badge can be seen
        // in the UI without having to create data manually first.
        var sarahId = Guid.Parse("30000000-0000-0000-0000-000000000001");

        var closedRecord = SicknessRecord.Create(
            Guid.Parse("71000000-0000-0000-0000-000000000001"),
            acmeId, sarahId, coldFluId,
            new DateOnly(2026, 5, 4), SicknessDayPart.FullDay,
            new DateOnly(2026, 5, 6), SicknessDayPart.FullDay,
            totalDays: 3m,
            notes: "Seasonal cold, recovered quickly.",
            evidenceStatus: SicknessEvidenceStatus.NotRequired,
            now: now);
        db.SicknessRecords.Add(closedRecord);

        var openStartDate = DateOnly.FromDateTime(now.UtcDateTime.AddDays(-10));
        var openRecord = SicknessRecord.Create(
            Guid.Parse("71000000-0000-0000-0000-000000000002"),
            acmeId, sarahId, backPainId,
            openStartDate, SicknessDayPart.FullDay,
            endDate: null, endDayPart: null,
            totalDays: null,
            notes: "Ongoing back pain, may need extended leave.",
            evidenceStatus: SicknessEvidenceStatus.Pending,
            now: now);
        db.SicknessRecords.Add(openRecord);

        await db.SaveChangesAsync();
    }
}
