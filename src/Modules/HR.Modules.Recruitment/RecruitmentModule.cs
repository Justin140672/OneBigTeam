using FluentValidation;
using Hangfire;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ApplyPositionProfileMatches;
using HR.Modules.Recruitment.Features.AssignVacancyPositionProfile;
using HR.Modules.Recruitment.Features.CloseVacancy;
using HR.Modules.Recruitment.Features.CloseVacancyOnEmployeePromoted;
using HR.Modules.Recruitment.Features.CreateApplication;
using HR.Modules.Recruitment.Features.CreateCandidate;
using HR.Modules.Recruitment.Features.CreateExternalRecruiter;
using HR.Modules.Recruitment.Features.CreateVacancy;
using HR.Modules.Recruitment.Features.DeleteCandidateDocument;
using HR.Modules.Recruitment.Features.DownloadCandidateDocument;
using HR.Modules.Recruitment.Features.GetApplication;
using HR.Modules.Recruitment.Features.GetApplicationsByStatus;
using HR.Modules.Recruitment.Features.GetCandidate;
using HR.Modules.Recruitment.Features.GetExternalRecruiter;
using HR.Modules.Recruitment.Features.GetExternalRecruiterActivitySummary;
using HR.Modules.Recruitment.Features.GetExternalRecruiterUsage;
using HR.Modules.Recruitment.Features.GetRecruitmentStageUsage;
using HR.Modules.Recruitment.Features.GetInterviewsTodayCount;
using HR.Modules.Recruitment.Features.GetPipelineSummary;
using HR.Modules.Recruitment.Features.GetRecruitmentKanban;
using HR.Modules.Recruitment.Features.GetStaleVacancies;
using HR.Modules.Recruitment.Features.GetVacanciesNeedingPositionProfileReview;
using HR.Modules.Recruitment.Features.GetUpcomingInterviews;
using HR.Modules.Recruitment.Features.GetVacancy;
using HR.Modules.Recruitment.Features.HireCandidate;
using HR.Modules.Recruitment.Features.ListApplicationsForVacancy;
using HR.Modules.Recruitment.Features.ListCandidateDocuments;
using HR.Modules.Recruitment.Features.ListCandidates;
using HR.Modules.Recruitment.Features.ListExternalRecruiters;
using HR.Modules.Recruitment.Features.ListInterviewsForVacancy;
using HR.Modules.Recruitment.Features.ListVacancies;
using HR.Modules.Recruitment.Features.MoveApplicationStage;
using HR.Modules.Recruitment.Features.OfferCandidate;
using HR.Modules.Recruitment.Features.PublishVacancy;
using HR.Modules.Recruitment.Features.RecordInterviewOutcome;
using HR.Modules.Recruitment.Features.RejectCandidate;
using HR.Modules.Recruitment.Features.ListRecruitmentStages;
using HR.Modules.Recruitment.Features.CreateRecruitmentStage;
using HR.Modules.Recruitment.Features.UpdateRecruitmentStage;
using HR.Modules.Recruitment.Features.ReorderRecruitmentStages;
using HR.Modules.Recruitment.Features.SetRecruitmentStageActiveStatus;
using HR.Modules.Recruitment.Features.ScheduleInterview;
using HR.Modules.Recruitment.Features.SetExternalRecruiterActiveStatus;
using HR.Modules.Recruitment.Features.UpdateCandidate;
using HR.Modules.Recruitment.Features.UpdateExternalRecruiter;
using HR.Modules.Recruitment.Features.UpdateInterview;
using HR.Modules.Recruitment.Features.UpdateVacancy;
using HR.Modules.Recruitment.Features.UploadCandidateDocument;
using HR.Modules.Recruitment.Features.WithdrawApplication;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Recruitment;

public static class RecruitmentModule
{
    public static IServiceCollection AddRecruitmentModule(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        AddFeatureServices(services);
        AddCandidateDocumentStorage(services, configuration);
        services.AddScoped<IInterviewFeedbackService, InterviewFeedbackService>();
        services.AddScoped<IWorkloadActionProvider, VacanciesAwaitingActionWorkloadActionProvider>();

        services.AddDbContext<RecruitmentDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "recruitment")));

        return services;
    }

    private static void AddCandidateDocumentStorage(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CandidateDocumentUploadOptions>(configuration.GetSection("Recruitment:CandidateDocuments"));
        services.AddScoped<ICandidateDocumentStorageService, LocalCandidateDocumentStorageService>();
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<RecruitmentStageChangeRecorder>();
        services.AddScoped<RecruitmentStageSeeder>();

        services.AddScoped<CreateVacancyHandler>();
        services.AddScoped<IValidator<CreateVacancyRequest>, CreateVacancyValidator>();

        services.AddScoped<GetVacancyHandler>();

        services.AddScoped<ListVacanciesHandler>();
        services.AddScoped<IValidator<ListVacanciesRequest>, ListVacanciesValidator>();

        services.AddScoped<UpdateVacancyHandler>();
        services.AddScoped<IValidator<UpdateVacancyRequest>, UpdateVacancyValidator>();

        services.AddScoped<CloseVacancyHandler>();
        services.AddScoped<IValidator<CloseVacancyRequest>, CloseVacancyValidator>();

        services.AddScoped<PublishVacancyHandler>();
        services.AddScoped<IValidator<PublishVacancyRequest>, PublishVacancyValidator>();

        services.AddScoped<HR.SharedKernel.IIntegrationEventHandler<HR.Modules.Employees.Contracts.EmployeePromotedIntegrationEvent>, EmployeePromotedHandler>();

        services.AddScoped<CreateCandidateHandler>();
        services.AddScoped<IValidator<CreateCandidateRequest>, CreateCandidateValidator>();

        services.AddScoped<GetCandidateHandler>();

        services.AddScoped<ListCandidatesHandler>();
        services.AddScoped<IValidator<ListCandidatesRequest>, ListCandidatesValidator>();

        services.AddScoped<UpdateCandidateHandler>();
        services.AddScoped<IValidator<UpdateCandidateRequest>, UpdateCandidateValidator>();

        services.AddScoped<CreateApplicationHandler>();
        services.AddScoped<IValidator<CreateApplicationRequest>, CreateApplicationValidator>();

        services.AddScoped<GetApplicationHandler>();

        services.AddScoped<ListApplicationsForVacancyHandler>();
        services.AddScoped<IValidator<ListApplicationsForVacancyRequest>, ListApplicationsForVacancyValidator>();

        services.AddScoped<GetRecruitmentKanbanHandler>();
        services.AddScoped<IValidator<GetRecruitmentKanbanRequest>, GetRecruitmentKanbanValidator>();

        services.AddScoped<MoveApplicationStageHandler>();
        services.AddScoped<IValidator<MoveApplicationStageRequest>, MoveApplicationStageValidator>();

        services.AddScoped<GetPipelineSummaryHandler>();
        services.AddScoped<IValidator<GetPipelineSummaryRequest>, GetPipelineSummaryValidator>();

        services.AddScoped<GetApplicationsByStatusHandler>();
        services.AddScoped<IValidator<GetApplicationsByStatusRequest>, GetApplicationsByStatusValidator>();

        services.AddScoped<WithdrawApplicationHandler>();
        services.AddScoped<IValidator<WithdrawApplicationRequest>, WithdrawApplicationValidator>();

        services.AddScoped<OfferCandidateHandler>();
        services.AddScoped<IValidator<OfferCandidateRequest>, OfferCandidateValidator>();

        services.AddScoped<RejectCandidateHandler>();
        services.AddScoped<IValidator<RejectCandidateRequest>, RejectCandidateValidator>();

        services.AddScoped<HireCandidateHandler>();
        services.AddScoped<IValidator<HireCandidateRequest>, HireCandidateValidator>();

        services.AddScoped<ScheduleInterviewHandler>();
        services.AddScoped<IValidator<ScheduleInterviewRequest>, ScheduleInterviewValidator>();

        services.AddScoped<UpdateInterviewHandler>();
        services.AddScoped<IValidator<UpdateInterviewRequest>, UpdateInterviewValidator>();

        services.AddScoped<InterviewOutcomeRecorder>();
        services.AddScoped<RecordInterviewOutcomeHandler>();
        services.AddScoped<IValidator<RecordInterviewOutcomeRequest>, RecordInterviewOutcomeValidator>();

        services.AddScoped<ListInterviewsForVacancyHandler>();
        services.AddScoped<IValidator<ListInterviewsForVacancyRequest>, ListInterviewsForVacancyValidator>();

        services.AddScoped<GetInterviewsTodayCountHandler>();
        services.AddScoped<IValidator<GetInterviewsTodayCountRequest>, GetInterviewsTodayCountValidator>();

        services.AddScoped<GetUpcomingInterviewsHandler>();
        services.AddScoped<GetStaleVacanciesHandler>();

        services.AddScoped<VacancyPositionProfileMatcher>();

        services.AddScoped<GetVacanciesNeedingPositionProfileReviewHandler>();

        services.AddScoped<ApplyPositionProfileMatchesHandler>();
        services.AddScoped<IValidator<ApplyPositionProfileMatchesRequest>, ApplyPositionProfileMatchesValidator>();

        services.AddScoped<AssignVacancyPositionProfileHandler>();
        services.AddScoped<IValidator<AssignVacancyPositionProfileRequest>, AssignVacancyPositionProfileValidator>();

        services.AddScoped<UploadCandidateDocumentHandler>();
        services.AddScoped<IValidator<UploadCandidateDocumentRequest>, UploadCandidateDocumentValidator>();

        services.AddScoped<ListCandidateDocumentsHandler>();
        services.AddScoped<IValidator<ListCandidateDocumentsRequest>, ListCandidateDocumentsValidator>();

        services.AddScoped<DownloadCandidateDocumentHandler>();

        services.AddScoped<DeleteCandidateDocumentHandler>();

        services.AddScoped<InterviewReminderJob>();
        services.AddScoped<OutstandingInterviewFeedbackReminderJob>();

        services.AddScoped<CreateExternalRecruiterHandler>();
        services.AddScoped<IValidator<CreateExternalRecruiterRequest>, CreateExternalRecruiterValidator>();

        services.AddScoped<ListExternalRecruitersHandler>();
        services.AddScoped<IValidator<ListExternalRecruitersRequest>, ListExternalRecruitersValidator>();

        services.AddScoped<GetExternalRecruiterHandler>();
        services.AddScoped<IValidator<GetExternalRecruiterRequest>, GetExternalRecruiterValidator>();

        services.AddScoped<UpdateExternalRecruiterHandler>();
        services.AddScoped<IValidator<UpdateExternalRecruiterRequest>, UpdateExternalRecruiterValidator>();

        services.AddScoped<SetExternalRecruiterActiveStatusHandler>();
        services.AddScoped<IValidator<SetExternalRecruiterActiveStatusRequest>, SetExternalRecruiterActiveStatusValidator>();

        services.AddScoped<GetExternalRecruiterUsageHandler>();

        services.AddScoped<GetExternalRecruiterActivitySummaryHandler>();
        services.AddScoped<IValidator<GetExternalRecruiterActivitySummaryRequest>, GetExternalRecruiterActivitySummaryValidator>();

        // Ticket #97: RecruitmentStage settings CRUD.
        services.AddScoped<ListRecruitmentStagesHandler>();

        services.AddScoped<CreateRecruitmentStageHandler>();
        services.AddScoped<IValidator<CreateRecruitmentStageRequest>, CreateRecruitmentStageValidator>();

        services.AddScoped<UpdateRecruitmentStageHandler>();
        services.AddScoped<IValidator<UpdateRecruitmentStageRequest>, UpdateRecruitmentStageValidator>();

        services.AddScoped<ReorderRecruitmentStagesHandler>();
        services.AddScoped<IValidator<ReorderRecruitmentStagesRequest>, ReorderRecruitmentStagesValidator>();

        services.AddScoped<SetRecruitmentStageActiveStatusHandler>();
        services.AddScoped<IValidator<SetRecruitmentStageActiveStatusRequest>, SetRecruitmentStageActiveStatusValidator>();

        services.AddScoped<GetRecruitmentStageUsageHandler>();

        services.AddScoped<HR.Infrastructure.Abstractions.IEmployeeRecruiterReader, Services.EmployeeRecruiterReader>();

        services.AddScoped<RecruitmentReportReader>();
        services.AddScoped<IRecruitmentPipelineReader>(sp => sp.GetRequiredService<RecruitmentReportReader>());
        services.AddScoped<IVacancyPerformanceReader>(sp => sp.GetRequiredService<RecruitmentReportReader>());
    }

    public static WebApplication UseRecruitmentRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        jobManager.AddOrUpdate<InterviewReminderJob>(
            "interview-reminders",
            job => job.ExecuteAsync(),
            Cron.Hourly());
        jobManager.AddOrUpdate<OutstandingInterviewFeedbackReminderJob>(
            "outstanding-interview-feedback-reminders",
            job => job.ExecuteAsync(),
            Cron.Daily(6));
        return app;
    }

    public static async Task MigrateRecruitmentAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS recruitment");
        await db.Database.MigrateAsync();
    }

    public static async Task SeedRecruitmentAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db      = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var seeder  = scope.ServiceProvider.GetRequiredService<RecruitmentStageSeeder>();
        var now     = DateTimeOffset.UtcNow;

        // Ticket #98: demo companies get the same default stage set every real company gets on
        // first use (see RecruitmentStageSeeder) — idempotent, so re-running seeding is safe.
        var acmeCompanyId    = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var betaCorpCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await seeder.EnsureDefaultStagesSeededAsync(acmeCompanyId, now, CancellationToken.None);
        await seeder.EnsureDefaultStagesSeededAsync(betaCorpCompanyId, now, CancellationToken.None);

        async Task<Dictionary<string, Guid>> GetStageIdsByNameAsync(Guid companyId) =>
            await db.RecruitmentStages
                .AsNoTracking()
                .Where(s => s.CompanyId == companyId)
                .ToDictionaryAsync(s => s.Name, s => s.Id);

        // ── Acme Corporation ─────────────────────────────────────────────────
        var acmeStages      = await GetStageIdsByNameAsync(acmeCompanyId);
        var acmeId          = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var acmeJamesId     = Guid.Parse("30000000-0000-0000-0000-000000000002"); // James Okafor
        var acmeLauraId     = Guid.Parse("30000000-0000-0000-0000-000000000005"); // Laura Bennett

        var acmeSeniorEngVacancyId = Guid.Parse("e0000000-0000-0000-0000-000000000001");
        var acmeHrBpVacancyId      = Guid.Parse("e0000000-0000-0000-0000-000000000002");
        var acmeDesignerVacancyId  = Guid.Parse("e0000000-0000-0000-0000-000000000003");

        // Position profile IDs are the same literal GUIDs seeded by EmployeesModule.SeedEmployeesAsync
        // for Acme (see posSenDevId/posHrAdvisorId/posDevId there) — the two modules cannot share a C#
        // constant since Recruitment has no reference to Employees, so the literals are duplicated by
        // convention, the same way department/employee IDs already are elsewhere in this method.
        var acmeSenSoftwareEngineerPositionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002"); // "Senior Software Engineer" (Engineering) — exact title+department match
        var acmeHrAdvisorPositionProfileId            = Guid.Parse("20000000-0000-0000-0000-000000000005"); // "HR Advisor" (People & HR) — closest existing role; no "HR Business Partner" profile exists, so this is a manual assignment rather than an automatic exact-title match
        var acmeSoftwareEngineerPositionProfileId     = Guid.Parse("20000000-0000-0000-0000-000000000003"); // "Software Engineer" (Engineering) — closest existing role; no "Product Designer" profile exists, so this is a manual assignment rather than an automatic exact-title match

        if (!await db.Vacancies.AnyAsync(v => v.CompanyId == acmeId))
        {
            // The department arguments that used to be passed here have been removed entirely (see
            // Vacancy.DepartmentId's removal) — department is now only ever known via the linked
            // Position Profile. seniorEngVacancy's AdvertTitle is deliberately left null (it's an
            // exact title match to its Position Profile, so this exercises the "no override — fall
            // back to Position Profile title" path); hrBpVacancy/designerVacancy keep a distinct
            // AdvertTitle since their linked Position Profile's title genuinely differs (see the
            // comments on acmeHrAdvisorPositionProfileId/acmeSoftwareEngineerPositionProfileId above).
            var seniorEngVacancy = Vacancy.Create(acmeSeniorEngVacancyId, acmeId, acmeSenSoftwareEngineerPositionProfileId, null, "Own delivery of core platform services.", acmeJamesId, now);
            seniorEngVacancy.Open(now, DateOnly.FromDateTime(now.UtcDateTime.AddDays(-14)));

            var hrBpVacancy = Vacancy.Create(acmeHrBpVacancyId, acmeId, acmeHrAdvisorPositionProfileId, "HR Business Partner", "Partner with department leads on people strategy.", acmeLauraId, now);

            var designerVacancy = Vacancy.Create(acmeDesignerVacancyId, acmeId, acmeSoftwareEngineerPositionProfileId, "Product Designer", "Own end-to-end design for the employee portal.", acmeJamesId, now);
            designerVacancy.Open(now, DateOnly.FromDateTime(now.UtcDateTime.AddDays(-60)));
            designerVacancy.Close(now, DateOnly.FromDateTime(now.UtcDateTime.AddDays(-5)));

            db.Vacancies.AddRange(seniorEngVacancy, hrBpVacancy, designerVacancy);
            await db.SaveChangesAsync();
        }

        var acmeEmmaId  = Guid.Parse("e1000000-0000-0000-0000-000000000001");
        var acmeLiamId  = Guid.Parse("e1000000-0000-0000-0000-000000000002");
        var acmeNoahId  = Guid.Parse("e1000000-0000-0000-0000-000000000003");
        var acmeOliviaId = Guid.Parse("e1000000-0000-0000-0000-000000000004");

        if (!await db.Candidates.AnyAsync(c => c.CompanyId == acmeId))
        {
            db.Candidates.AddRange(
                Candidate.Create(acmeEmmaId,   acmeId, "Emma",   "Clarke", "emma.clarke@example.com",  "+44 7700 900001", null, now),
                Candidate.Create(acmeLiamId,   acmeId, "Liam",   "Turner", "liam.turner@example.com",  "+44 7700 900002", null, now),
                Candidate.Create(acmeNoahId,   acmeId, "Noah",   "Patel",  "noah.patel@example.com",   "+44 7700 900003", null, now),
                Candidate.Create(acmeOliviaId, acmeId, "Olivia", "Grant",  "olivia.grant@example.com", "+44 7700 900004", null, now));

            await db.SaveChangesAsync();
        }

        if (!await db.Applications.AnyAsync(a => a.CompanyId == acmeId))
        {
            var emmaApplication = Application.Create(Guid.Parse("e2000000-0000-0000-0000-000000000001"), acmeId, acmeSeniorEngVacancyId, acmeEmmaId, acmeStages["CV Review"], null, now);
            emmaApplication.MoveToStage(acmeStages["Interview"], now);

            var liamApplication = Application.Create(Guid.Parse("e2000000-0000-0000-0000-000000000002"), acmeId, acmeSeniorEngVacancyId, acmeLiamId, acmeStages["CV Review"], null, now);
            liamApplication.MoveToStage(acmeStages["Interview"], now);
            liamApplication.SetInterviewOutcome(InterviewOutcome.Passed, now);

            var noahApplication = Application.Create(Guid.Parse("e2000000-0000-0000-0000-000000000003"), acmeId, acmeSeniorEngVacancyId, acmeNoahId, acmeStages["CV Review"], "Not enough backend experience for this role.", now);
            noahApplication.RecordRejection(acmeStages["Rejected"], "Not enough backend experience for this role.", now);

            var oliviaApplication = Application.Create(Guid.Parse("e2000000-0000-0000-0000-000000000004"), acmeId, acmeDesignerVacancyId, acmeOliviaId, acmeStages["CV Review"], null, now);
            oliviaApplication.MoveToStage(acmeStages["Interview"], now);
            oliviaApplication.SetInterviewOutcome(InterviewOutcome.Passed, now);
            oliviaApplication.MoveToStage(acmeStages["Offer"], now);
            oliviaApplication.RecordHire(acmeStages["Hired"], now);

            db.Applications.AddRange(emmaApplication, liamApplication, noahApplication, oliviaApplication);
            await db.SaveChangesAsync();
        }

        // ── Beta Corp ─────────────────────────────────────────────────────────
        var betaStages    = await GetStageIdsByNameAsync(betaCorpCompanyId);
        var betaCorpId    = betaCorpCompanyId;
        var betaAliceId   = Guid.Parse("30000000-0000-0000-0000-000000000011"); // Alice Morgan

        var betaBackendVacancyId = Guid.Parse("e0000000-0000-0000-0000-000000000011");

        // Same literal-GUID convention as the Acme block above: matches betaPosDevId ("Software
        // Developer") in EmployeesModule.SeedEmployeesAsync. No "Backend Engineer" profile exists for
        // Beta, so this is a manual assignment rather than an automatic exact-title match.
        var betaSoftwareDeveloperPositionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000012");

        if (!await db.Vacancies.AnyAsync(v => v.CompanyId == betaCorpId))
        {
            var backendVacancy = Vacancy.Create(betaBackendVacancyId, betaCorpId, betaSoftwareDeveloperPositionProfileId, "Backend Engineer", "Build and scale our payments platform.", betaAliceId, now);
            backendVacancy.Open(now, DateOnly.FromDateTime(now.UtcDateTime.AddDays(-7)));

            db.Vacancies.Add(backendVacancy);
            await db.SaveChangesAsync();
        }

        var betaSophieId = Guid.Parse("e1000000-0000-0000-0000-000000000011");
        var betaEthanId  = Guid.Parse("e1000000-0000-0000-0000-000000000012");

        if (!await db.Candidates.AnyAsync(c => c.CompanyId == betaCorpId))
        {
            db.Candidates.AddRange(
                Candidate.Create(betaSophieId, betaCorpId, "Sophie", "Bennett", "sophie.bennett@example.com", "+44 7700 900011", null, now),
                Candidate.Create(betaEthanId,  betaCorpId, "Ethan",  "Wright",  "ethan.wright@example.com",   "+44 7700 900012", null, now));

            await db.SaveChangesAsync();
        }

        if (!await db.Applications.AnyAsync(a => a.CompanyId == betaCorpId))
        {
            var sophieApplication = Application.Create(Guid.Parse("e2000000-0000-0000-0000-000000000011"), betaCorpId, betaBackendVacancyId, betaSophieId, betaStages["Application Received"], null, now);

            var ethanApplication = Application.Create(Guid.Parse("e2000000-0000-0000-0000-000000000012"), betaCorpId, betaBackendVacancyId, betaEthanId, betaStages["CV Review"], null, now);

            db.Applications.AddRange(sophieApplication, ethanApplication);
            await db.SaveChangesAsync();
        }
    }
}
