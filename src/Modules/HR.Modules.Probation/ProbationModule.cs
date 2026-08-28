using HR.Modules.Tasks.Contracts;
using FluentValidation;
using Hangfire;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.CompleteProbationReview;
using HR.Modules.Probation.Features.CompleteProbationReviewFromTask;
using HR.Modules.Probation.Features.CreateProbationOnEmployeeCreated;
using HR.Modules.Probation.Features.ReassignReviewsOnManagerChanged;
using HR.Modules.Probation.Features.CreateProbationRecord;
using HR.Modules.Probation.Features.CreateProbationReview;
using HR.Modules.Probation.Features.GetProbationRecord;
using HR.Modules.Probation.Features.GetProbationRecordByEmployee;
using HR.Modules.Probation.Features.GetProbationRecordAuditHistory;
using HR.Modules.Probation.Features.GetProbationReview;
using HR.Modules.Probation.Features.GetProbationReviews;
using HR.Modules.Probation.Features.GetMyProbationStatus;
using HR.Modules.Probation.Features.GetProbationStatus;
using HR.Modules.Probation.Features.GetUpcomingProbationReviews;
using HR.Modules.Probation.Features.MarkProbationNotApplicable;
using HR.Modules.Probation.Features.UpdateProbationRecord;
using HR.Modules.Probation.Jobs;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Probation;

public static class ProbationModule
{
    public static IServiceCollection AddProbationModule(
        this IServiceCollection services,
        string connectionString)
    {
        AddFeatureServices(services);

        services.AddScoped<IWorkloadActionProvider, Services.ProbationReviewsDueWorkloadActionProvider>();
        services.AddScoped<IWorkloadActionProvider, Services.OverdueProbationReviewsWorkloadActionProvider>();

        services.AddDbContext<ProbationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "probation")));

        return services;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<CreateProbationRecordHandler>();
        services.AddScoped<IValidator<CreateProbationRecordRequest>, CreateProbationRecordValidator>();
        services.AddScoped<GetProbationRecordHandler>();
        services.AddScoped<GetProbationRecordByEmployeeHandler>();
        services.AddScoped<GetProbationRecordAuditHistoryHandler>();
        services.AddScoped<GetProbationStatusHandler>();
        services.AddScoped<GetMyProbationStatusHandler>();
        services.AddScoped<GetProbationReviewHandler>();
        services.AddScoped<UpdateProbationRecordHandler>();
        services.AddScoped<IValidator<UpdateProbationRecordRequest>, UpdateProbationRecordValidator>();
        services.AddScoped<MarkProbationNotApplicableHandler>();
        services.AddScoped<IValidator<MarkProbationNotApplicableRequest>, MarkProbationNotApplicableValidator>();
        services.AddScoped<CreateProbationReviewHandler>();
        services.AddScoped<IValidator<CreateProbationReviewRequest>, CreateProbationReviewValidator>();
        services.AddScoped<GetProbationReviewsHandler>();
        services.AddScoped<GetUpcomingProbationReviewsHandler>();
        services.AddScoped<CompleteProbationReviewHandler>();
        services.AddScoped<IValidator<CompleteProbationReviewRequest>, CompleteProbationReviewValidator>();
        services.AddScoped<ITaskCompletionAction, CompleteProbationReviewFromTaskAction>();
        services.AddScoped<Services.ProbationExtensionService>();
        services.AddScoped<Services.ProbationReviewRecalculationService>();
        services.AddScoped<IProbationSummaryReader, ProbationSummaryReader>();
        services.AddScoped<IProbationStatusReader, ProbationStatusReader>();
        services.AddScoped<IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>, EmployeeCreatedHandler>();
        services.AddScoped<IIntegrationEventHandler<EmployeeManagerChangedIntegrationEvent>, ManagerChangedHandler>();
        services.AddScoped<GenerateDueProbationReviewsJob>();
        services.AddScoped<IProbationHistoryReplayer, ProbationHistoryReplayer>();
        services.AddScoped<IProbationReportReader, ProbationReportReader>();
        services.AddScoped<Services.ProbationResourceAuthorizer>();
    }

    public static WebApplication UseProbationRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        jobManager.AddOrUpdate<GenerateDueProbationReviewsJob>(
            "generate-due-probation-reviews",
            job => job.ExecuteAsync(),
            Cron.Daily(1));
        return app;
    }

    public static async Task MigrateProbationAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbationDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS probation");
        await db.Database.MigrateAsync();
    }

    public static async Task SeedProbationAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbationDbContext>();

        if (await db.ProbationRecords.AnyAsync())
            return;

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var acmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var betaId = Guid.Parse("00000000-0000-0000-0000-000000000002");

        // All seeded employees that have a manager (employeeId, managerId, companyId, startDate)
        (Guid companyId, Guid employeeId, Guid managerId, DateOnly startDate)[] entries =
        [
            (acmeId, Guid.Parse("30000000-0000-0000-0000-000000000002"), Guid.Parse("30000000-0000-0000-0000-000000000001"), new DateOnly(2021, 3,  15)),
            (acmeId, Guid.Parse("30000000-0000-0000-0000-000000000003"), Guid.Parse("30000000-0000-0000-0000-000000000001"), new DateOnly(2021, 9,   1)),
            (acmeId, Guid.Parse("30000000-0000-0000-0000-000000000004"), Guid.Parse("30000000-0000-0000-0000-000000000002"), new DateOnly(2023, 2,  20)),
            (acmeId, Guid.Parse("30000000-0000-0000-0000-000000000006"), Guid.Parse("30000000-0000-0000-0000-000000000005"), new DateOnly(2022, 11,  7)),
            (acmeId, Guid.Parse("30000000-0000-0000-0000-000000000009"), Guid.Parse("30000000-0000-0000-0000-000000000008"), new DateOnly(2023, 5,   2)),
            (acmeId, Guid.Parse("30000000-0000-0000-0000-000000000010"), Guid.Parse("30000000-0000-0000-0000-000000000008"), new DateOnly(2024, 1,   8)),
            (betaId, Guid.Parse("30000000-0000-0000-0000-000000000012"), Guid.Parse("30000000-0000-0000-0000-000000000011"), new DateOnly(2023, 9,   4)),
        ];

        for (var i = 0; i < entries.Length; i++)
        {
            var (companyId, employeeId, managerId, startDate) = entries[i];
            var expectedEnd = startDate.AddMonths(3);
            var recordId    = new Guid($"40000000-0000-0000-0000-{i + 1:D12}");

            var record = ProbationRecord.Create(recordId, companyId, employeeId, managerId,
                startDate, expectedEnd, null, today, now);
            record.Pass(managerId, expectedEnd, "Probation completed successfully.", now);
            db.ProbationRecords.Add(record);

            var checkIn = ProbationReview.Create(
                new Guid($"50000000-0000-0000-0000-{i * 3 + 1:D12}"),
                companyId, recordId, ProbationReviewType.ManagerCheckIn,
                startDate.AddMonths(1), now);
            checkIn.Complete(managerId, null, null, now);
            db.ProbationReviews.Add(checkIn);

            var hrReview = ProbationReview.Create(
                new Guid($"50000000-0000-0000-0000-{i * 3 + 2:D12}"),
                companyId, recordId, ProbationReviewType.HrReview,
                startDate.AddMonths(2), now);
            hrReview.Complete(managerId, null, null, now);
            db.ProbationReviews.Add(hrReview);

            var finalReview = ProbationReview.Create(
                new Guid($"50000000-0000-0000-0000-{i * 3 + 3:D12}"),
                companyId, recordId, ProbationReviewType.FinalDecision,
                expectedEnd, now);
            finalReview.Complete(managerId, ProbationOutcome.Pass, "Probation passed.", now);
            db.ProbationReviews.Add(finalReview);
        }

        // Active probation — Carlos Rivera on probation under James Okafor.
        // ManagerCheckIn is overdue; record is ReviewDue.
        // Fixed IDs so the UI/E2E tests can navigate directly to this review.
        var activeRecordId = Guid.Parse("40000000-0000-0000-0000-000000000010");
        var activeReviewId = Guid.Parse("50000000-0000-0000-0000-000000000100");
        var empCarlosId    = Guid.Parse("30000000-0000-0000-0000-000000000010");
        var empJamesId     = Guid.Parse("30000000-0000-0000-0000-000000000002");

        var activeRecord = ProbationRecord.Create(
            activeRecordId, acmeId, empCarlosId, empJamesId,
            new DateOnly(2026, 4, 7), new DateOnly(2026, 7, 7), null, today, now);
        activeRecord.MarkReviewDue(now);
        db.ProbationRecords.Add(activeRecord);

        db.ProbationReviews.Add(ProbationReview.Create(
            activeReviewId, acmeId, activeRecordId,
            ProbationReviewType.ManagerCheckIn, new DateOnly(2026, 5, 7), now));

        // Second, independent active probation — Sophie Laurent under Sarah Chen.
        // Sophie does not appear in the completed-probation-loop `entries` array above and is
        // not the Carlos Rivera active scenario, so this record/review is not shared with any
        // other seeded scenario. Kept deliberately separate from the Carlos Rivera review so
        // that E2E tests which complete a review (ProbationReviewFlowTests) don't mutate the
        // same review that other E2E tests (ProbationReviewTaskTests) rely on staying open.
        // Fixed IDs so the UI/E2E tests can navigate directly to this review.
        var activeRecord2Id = Guid.Parse("40000000-0000-0000-0000-000000000011");
        var activeReview2Id = Guid.Parse("50000000-0000-0000-0000-000000000101");
        var empSophieId     = Guid.Parse("30000000-0000-0000-0000-000000000007");
        var empSarahId      = Guid.Parse("30000000-0000-0000-0000-000000000001");

        var activeRecord2 = ProbationRecord.Create(
            activeRecord2Id, acmeId, empSophieId, empSarahId,
            new DateOnly(2026, 4, 7), new DateOnly(2026, 7, 7), null, today, now);
        activeRecord2.MarkReviewDue(now);
        db.ProbationRecords.Add(activeRecord2);

        db.ProbationReviews.Add(ProbationReview.Create(
            activeReview2Id, acmeId, activeRecord2Id,
            ProbationReviewType.ManagerCheckIn, new DateOnly(2026, 5, 7), now));

        await db.SaveChangesAsync();
    }
}
