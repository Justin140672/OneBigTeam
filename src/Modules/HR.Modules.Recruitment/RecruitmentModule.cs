using FluentValidation;
using Hangfire;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.CloseVacancy;
using HR.Modules.Recruitment.Features.CreateApplication;
using HR.Modules.Recruitment.Features.CreateCandidate;
using HR.Modules.Recruitment.Features.CreateVacancy;
using HR.Modules.Recruitment.Features.DeleteCandidateDocument;
using HR.Modules.Recruitment.Features.DownloadCandidateDocument;
using HR.Modules.Recruitment.Features.GetApplication;
using HR.Modules.Recruitment.Features.GetApplicationsByStatus;
using HR.Modules.Recruitment.Features.GetCandidate;
using HR.Modules.Recruitment.Features.GetInterviewsTodayCount;
using HR.Modules.Recruitment.Features.GetPipelineSummary;
using HR.Modules.Recruitment.Features.GetStaleVacancies;
using HR.Modules.Recruitment.Features.GetUpcomingInterviews;
using HR.Modules.Recruitment.Features.GetVacancy;
using HR.Modules.Recruitment.Features.HireCandidate;
using HR.Modules.Recruitment.Features.ListApplicationsForVacancy;
using HR.Modules.Recruitment.Features.ListCandidateDocuments;
using HR.Modules.Recruitment.Features.ListCandidates;
using HR.Modules.Recruitment.Features.ListInterviewsForVacancy;
using HR.Modules.Recruitment.Features.ListVacancies;
using HR.Modules.Recruitment.Features.OfferCandidate;
using HR.Modules.Recruitment.Features.RecordInterviewOutcome;
using HR.Modules.Recruitment.Features.RejectCandidate;
using HR.Modules.Recruitment.Features.ScheduleInterview;
using HR.Modules.Recruitment.Features.UpdateCandidate;
using HR.Modules.Recruitment.Features.UpdateInterview;
using HR.Modules.Recruitment.Features.UpdateVacancy;
using HR.Modules.Recruitment.Features.UploadCandidateDocument;
using HR.Modules.Recruitment.Features.WithdrawApplication;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
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
        services.AddScoped<CreateVacancyHandler>();
        services.AddScoped<IValidator<CreateVacancyRequest>, CreateVacancyValidator>();

        services.AddScoped<GetVacancyHandler>();

        services.AddScoped<ListVacanciesHandler>();
        services.AddScoped<IValidator<ListVacanciesRequest>, ListVacanciesValidator>();

        services.AddScoped<UpdateVacancyHandler>();
        services.AddScoped<IValidator<UpdateVacancyRequest>, UpdateVacancyValidator>();

        services.AddScoped<CloseVacancyHandler>();
        services.AddScoped<IValidator<CloseVacancyRequest>, CloseVacancyValidator>();

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

        services.AddScoped<UploadCandidateDocumentHandler>();
        services.AddScoped<IValidator<UploadCandidateDocumentRequest>, UploadCandidateDocumentValidator>();

        services.AddScoped<ListCandidateDocumentsHandler>();
        services.AddScoped<IValidator<ListCandidateDocumentsRequest>, ListCandidateDocumentsValidator>();

        services.AddScoped<DownloadCandidateDocumentHandler>();

        services.AddScoped<DeleteCandidateDocumentHandler>();

        services.AddScoped<InterviewReminderJob>();
        services.AddScoped<OutstandingInterviewFeedbackReminderJob>();
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
        var db  = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var now = DateTimeOffset.UtcNow;

        // ── Acme Corporation ─────────────────────────────────────────────────
        var acmeId          = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var acmeEngDeptId   = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var acmeHrDeptId    = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var acmeJamesId     = Guid.Parse("30000000-0000-0000-0000-000000000002"); // James Okafor
        var acmeLauraId     = Guid.Parse("30000000-0000-0000-0000-000000000005"); // Laura Bennett

        var acmeSeniorEngVacancyId = Guid.Parse("e0000000-0000-0000-0000-000000000001");
        var acmeHrBpVacancyId      = Guid.Parse("e0000000-0000-0000-0000-000000000002");
        var acmeDesignerVacancyId  = Guid.Parse("e0000000-0000-0000-0000-000000000003");

        if (!await db.Vacancies.AnyAsync(v => v.CompanyId == acmeId))
        {
            var seniorEngVacancy = Vacancy.Create(acmeSeniorEngVacancyId, acmeId, acmeEngDeptId, "Senior Software Engineer", "Own delivery of core platform services.", "Remote", acmeJamesId, now);
            seniorEngVacancy.Open(now, DateOnly.FromDateTime(now.UtcDateTime.AddDays(-14)));

            var hrBpVacancy = Vacancy.Create(acmeHrBpVacancyId, acmeId, acmeHrDeptId, "HR Business Partner", "Partner with department leads on people strategy.", "London", acmeLauraId, now);

            var designerVacancy = Vacancy.Create(acmeDesignerVacancyId, acmeId, acmeEngDeptId, "Product Designer", "Own end-to-end design for the employee portal.", "Remote", acmeJamesId, now);
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
            var emmaApplication = Application.Create(Guid.Parse("e2000000-0000-0000-0000-000000000001"), acmeId, acmeSeniorEngVacancyId, acmeEmmaId, null, now);
            emmaApplication.MoveToScreening(now);
            emmaApplication.ScheduleInterview(now);

            var liamApplication = Application.Create(Guid.Parse("e2000000-0000-0000-0000-000000000002"), acmeId, acmeSeniorEngVacancyId, acmeLiamId, null, now);
            liamApplication.MoveToScreening(now);
            liamApplication.ScheduleInterview(now);
            liamApplication.RecordInterviewOutcome(InterviewOutcome.Passed, now);

            var noahApplication = Application.Create(Guid.Parse("e2000000-0000-0000-0000-000000000003"), acmeId, acmeSeniorEngVacancyId, acmeNoahId, "Not enough backend experience for this role.", now);
            noahApplication.MoveToScreening(now);
            noahApplication.Reject(now);

            var oliviaApplication = Application.Create(Guid.Parse("e2000000-0000-0000-0000-000000000004"), acmeId, acmeDesignerVacancyId, acmeOliviaId, null, now);
            oliviaApplication.MoveToScreening(now);
            oliviaApplication.ScheduleInterview(now);
            oliviaApplication.RecordInterviewOutcome(InterviewOutcome.Passed, now);
            oliviaApplication.Offer(now);
            oliviaApplication.Hire(now);

            db.Applications.AddRange(emmaApplication, liamApplication, noahApplication, oliviaApplication);
            await db.SaveChangesAsync();
        }

        // ── Beta Corp ─────────────────────────────────────────────────────────
        var betaCorpId    = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var betaEngDeptId = Guid.Parse("10000000-0000-0000-0000-000000000011");
        var betaAliceId   = Guid.Parse("30000000-0000-0000-0000-000000000011"); // Alice Morgan

        var betaBackendVacancyId = Guid.Parse("e0000000-0000-0000-0000-000000000011");

        if (!await db.Vacancies.AnyAsync(v => v.CompanyId == betaCorpId))
        {
            var backendVacancy = Vacancy.Create(betaBackendVacancyId, betaCorpId, betaEngDeptId, "Backend Engineer", "Build and scale our payments platform.", "Remote", betaAliceId, now);
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
            var sophieApplication = Application.Create(Guid.Parse("e2000000-0000-0000-0000-000000000011"), betaCorpId, betaBackendVacancyId, betaSophieId, null, now);

            var ethanApplication = Application.Create(Guid.Parse("e2000000-0000-0000-0000-000000000012"), betaCorpId, betaBackendVacancyId, betaEthanId, null, now);
            ethanApplication.MoveToScreening(now);

            db.Applications.AddRange(sophieApplication, ethanApplication);
            await db.SaveChangesAsync();
        }
    }
}
