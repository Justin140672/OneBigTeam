using HR.Modules.Tasks.Contracts;
using Hangfire;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.CloseSicknessRecord;
using HR.Modules.Sickness.Features.CompleteReturnToWorkReview;
using HR.Modules.Sickness.Features.CompleteReturnToWorkReviewFromTask;
using HR.Modules.Sickness.Features.FulfilEvidenceRequest;
using HR.Modules.Sickness.Features.CreateSicknessCategory;
using HR.Modules.Sickness.Features.DeactivateSicknessCategory;
using HR.Modules.Sickness.Features.GetCurrentSicknessAbsences;
using HR.Modules.Sickness.Features.GetMissingFitNotes;
using HR.Modules.Sickness.Features.GetMySicknessRecords;
using HR.Modules.Sickness.Features.GetOverdueReturnToWorkReviews;
using HR.Modules.Sickness.Features.GetReturnToWorkReview;
using HR.Modules.Sickness.Features.GetSicknessRecord;
using HR.Modules.Sickness.Features.GetSicknessRecordAuditHistory;
using HR.Modules.Sickness.Features.GetTeamSicknessToday;
using HR.Modules.Sickness.Features.ListAttendanceAlerts;
using HR.Modules.Sickness.Features.ListEmployeeSicknessRecords;
using HR.Modules.Sickness.Features.ListSicknessCategories;
using HR.Modules.Sickness.Features.RecordMySickness;
using HR.Modules.Sickness.Features.RecordSickness;
using HR.Modules.Sickness.Features.UpdateSicknessRecord;
using HR.Modules.Sickness.Features.UpdateSicknessCategory;
using HR.Modules.Sickness.Jobs;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.Infrastructure.Abstractions;
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

        services.AddScoped<IWorkloadActionProvider, SicknessPendingActionsWorkloadActionProvider>();

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
        services.AddScoped<GetSicknessRecordAuditHistoryHandler>();
        services.AddScoped<ListEmployeeSicknessRecordsHandler>();
        services.AddScoped<UpdateSicknessRecordHandler>();
        services.AddScoped<CloseSicknessRecordHandler>();
        services.AddScoped<GetCurrentSicknessAbsencesHandler>();
        services.AddScoped<GetTeamSicknessTodayHandler>();
        services.AddScoped<GetOverdueReturnToWorkReviewsHandler>();
        services.AddScoped<GetMissingFitNotesHandler>();
        services.AddScoped<GetReturnToWorkReviewHandler>();
        services.AddScoped<CompleteReturnToWorkReviewHandler>();
        services.AddScoped<GetMySicknessRecordsHandler>();
        services.AddScoped<Services.FitNoteEvidenceRequestService>();
        services.AddScoped<FitNoteRequestJob>();
        services.AddScoped<SicknessEvidenceReminderJob>();
        services.AddScoped<ReturnToWorkReminderJob>();
        services.AddScoped<AttendanceAlertEvaluationService>();
        services.AddScoped<AttendanceAlertEvaluationJob>();
        services.AddScoped<ListAttendanceAlertsHandler>();
        services.AddScoped<ITaskCompletionAction, SicknessEvidenceUploadCompletionAction>();
        services.AddScoped<ITaskCompletionAction, CompleteReturnToWorkReviewFromTaskAction>();
        services.AddScoped<IEmployeeSicknessStatusReader, EmployeeSicknessStatusReader>();
        services.AddScoped<IEmployeesOffSickReader, EmployeesOffSickReader>();
        services.AddScoped<IEmployeesMissingFitNoteReader, EmployeesMissingFitNoteReader>();
        services.AddScoped<ISicknessCategoryDefaultsProvisioner, SicknessCategoryDefaultsProvisioner>();
        services.AddScoped<ISicknessReportReader, SicknessReportReader>();
        services.AddScoped<HR.Infrastructure.Abstractions.ISicknessDataExportSource, Services.SicknessDataExportSource>();
        services.AddScoped<Services.SicknessResourceAuthorizer>();
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
            // CancellationToken.None is replaced by Hangfire with a live shutdown/abort token at run time.
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Daily(4));
        jobManager.AddOrUpdate<ReturnToWorkReminderJob>(
            "return-to-work-reminders",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Daily(5));
        jobManager.AddOrUpdate<AttendanceAlertEvaluationJob>(
            "attendance-alert-evaluation",
            job => job.ExecuteAsync(),
            Cron.Daily(6));
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

        var now    = DateTimeOffset.UtcNow;
        var acmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        // Scoped to Acme specifically — sickness_categories is a multi-tenant table (CompanyId
        // column), and other companies (self-service SignUp via CompanyDefaultDataSeeder /
        // SicknessCategoryDefaultsProvisioner, or the dev-seeded "betaCorp" company in
        // CompaniesModule) may already have their own category rows with unrelated ids. An
        // unscoped AnyAsync() here would treat those as "Acme already seeded", skip creating
        // Acme's fixed-id categories below, and then the Tom Williams seed (which references the
        // fixed illnessId) would fail its FK constraint because that id was never created for
        // ANY company.
        if (await db.SicknessCategories.AnyAsync(c => c.CompanyId == acmeId))
        {
            // DSH-05's Tom Williams "Missing fit notes" demo data (below, guarded separately by
            // its own id check) must still be seeded even on a database that was already fully
            // seeded before this fix existed — this broad "categories already exist" branch only
            // skips the ORIGINAL seed content below, which this later addition depends on (its
            // SicknessRecord references the "Illness" category, so it can only run once that
            // category is guaranteed to exist — never before the block that creates it).
            await SeedTomWilliamsMissingFitNoteDemoDataAsync(db);
            return;
        }

        // SICK-05: mirrors SicknessCategoryDefaultsProvisioner's broad, non-diagnostic category set
        // (see that class's doc comment) so dev/E2E data matches what production provisions.
        var illnessId    = Guid.Parse("70000000-0000-0000-0000-000000000001");
        var injuryId     = Guid.Parse("70000000-0000-0000-0000-000000000002");
        var mentalHealthId  = Guid.Parse("70000000-0000-0000-0000-000000000003");
        var appointmentId   = Guid.Parse("70000000-0000-0000-0000-000000000004");
        var dependantCareId = Guid.Parse("70000000-0000-0000-0000-000000000005");
        var otherId         = Guid.Parse("70000000-0000-0000-0000-000000000006");

        db.SicknessCategories.Add(SicknessCategory.Create(illnessId,      acmeId, "Illness", 1, now));
        db.SicknessCategories.Add(SicknessCategory.Create(injuryId,       acmeId, "Injury", 2, now));
        db.SicknessCategories.Add(SicknessCategory.Create(mentalHealthId, acmeId, "Mental health", 3, now));
        db.SicknessCategories.Add(SicknessCategory.Create(appointmentId,  acmeId, "Medical appointment", 4, now));
        db.SicknessCategories.Add(SicknessCategory.Create(dependantCareId, acmeId, "Dependant care", 5, now));
        db.SicknessCategories.Add(SicknessCategory.Create(otherId,        acmeId, "Other", 6, now));

        // Sarah Chen (CTO) — a closed record and a currently-open one, so both the
        // Active/Closed status badge and the fit-note evidence badge can be seen
        // in the UI without having to create data manually first.
        var sarahId = Guid.Parse("30000000-0000-0000-0000-000000000001");

        var closedRecord = SicknessRecord.Create(
            Guid.Parse("71000000-0000-0000-0000-000000000001"),
            acmeId, sarahId, illnessId,
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
            acmeId, sarahId, injuryId,
            openStartDate, SicknessDayPart.FullDay,
            endDate: null, endDayPart: null,
            totalDays: null,
            notes: "Ongoing back pain, may need extended leave.",
            evidenceStatus: SicknessEvidenceStatus.Pending,
            now: now);
        db.SicknessRecords.Add(openRecord);

        await db.SaveChangesAsync();

        // Categories now exist (just created above), so it's safe to seed Tom's record — see the
        // matching comment in the "already seeded" branch above for why this can't run earlier.
        await SeedTomWilliamsMissingFitNoteDemoDataAsync(db);
    }

    // DSH-05: Tom Williams (James Okafor's only direct report) needs a live, non-day-of-week-
    // dependent reason for the Manager Dashboard "Team Status" widget's "Missing fit notes" tile
    // to be non-zero for James's team (see TeamStatusSummaryTests). Idempotent and always called
    // regardless of the broad "already seeded" guard above — a database seeded before this fix
    // existed would otherwise never receive it. Dates are computed relative to "now" rather than
    // fixed, so this stays true no matter when the seed actually runs. laura.bennett (HR Manager)
    // is the requester, mirroring how a real evidence request would be raised by HR.
    private static async Task SeedTomWilliamsMissingFitNoteDemoDataAsync(SicknessDbContext db)
    {
        var tomOpenRecordId = Guid.Parse("71000000-0000-0000-0000-000000000003");

        if (await db.SicknessRecords.AnyAsync(r => r.Id == tomOpenRecordId))
            return;

        var now      = DateTimeOffset.UtcNow;
        var acmeId   = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var illnessId = Guid.Parse("70000000-0000-0000-0000-000000000001");
        var tomId    = Guid.Parse("30000000-0000-0000-0000-000000000004");
        var lauraId  = Guid.Parse("30000000-0000-0000-0000-000000000005");

        // Defence in depth against the FK violation this method previously caused: even though the
        // caller now checks Acme's categories specifically (see SeedSicknessAsync above), guarantee
        // the exact category row this record references actually exists before inserting, rather
        // than assuming it does based on a broader "categories exist" check elsewhere.
        if (!await db.SicknessCategories.AnyAsync(c => c.Id == illnessId))
        {
            db.SicknessCategories.Add(SicknessCategory.Create(illnessId, acmeId, "Illness", 1, now));
            await db.SaveChangesAsync();
        }

        // Deliberately seeded as a CLOSED record (endDate set), not an open/active one: an
        // earlier version of this seed left it open, which made Tom's Manager Dashboard status
        // badge show "Sick" and broke ManagerDashboardTests.
        // MyTeamWidget_ShowsAtWorkStatusBadge_ForDirectReportWithNoActiveSicknessOrLeave, which
        // relies on Tom (James Okafor's only direct report) having no ACTIVE sickness/leave.
        // "Missing fit notes" only depends on the evidence request's own Pending/Overdue status
        // (see EmployeesMissingFitNoteReader / GetMissingFitNotesHandler — both join on the
        // record purely for EmployeeId and never filter by the record's Status), so closing this
        // record keeps the fit-note tile non-zero for James's team while Tom's own status reverts
        // to "At Work".
        var tomOpenStartDate = DateOnly.FromDateTime(now.UtcDateTime.AddDays(-5));
        var tomClosedDate = DateOnly.FromDateTime(now.UtcDateTime.AddDays(-1));
        var tomOpenRecord = SicknessRecord.Create(
            tomOpenRecordId,
            acmeId, tomId, illnessId,
            tomOpenStartDate, SicknessDayPart.FullDay,
            endDate: tomClosedDate, endDayPart: SicknessDayPart.FullDay,
            totalDays: 4m,
            notes: "Flu, awaiting fit note.",
            evidenceStatus: SicknessEvidenceStatus.Pending,
            now: now);
        db.SicknessRecords.Add(tomOpenRecord);

        var tomEvidenceRequest = SicknessEvidenceRequest.Create(
            Guid.Parse("72000000-0000-0000-0000-000000000001"),
            acmeId, tomOpenRecord.Id, lauraId,
            dueDate: DateOnly.FromDateTime(now.UtcDateTime.AddDays(2)),
            notes: "Please provide a fit note for your absence.",
            now: now);
        db.SicknessEvidenceRequests.Add(tomEvidenceRequest);

        await db.SaveChangesAsync();
    }
}
