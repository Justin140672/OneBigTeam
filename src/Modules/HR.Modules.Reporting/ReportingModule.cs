using FluentValidation;
using HR.Modules.Reporting.Features.AddReportFavourite;
using HR.Modules.Reporting.Features.DeleteReportView;
using HR.Modules.Reporting.Features.ExportAssetAssignmentReport;
using HR.Modules.Reporting.Features.ExportEmployeeDirectoryReport;
using HR.Modules.Reporting.Features.ExportEmployeeLeaverReport;
using HR.Modules.Reporting.Features.ExportEmployeeStarterReport;
using HR.Modules.Reporting.Features.ExportHrHeadcountSummaryReport;
using HR.Modules.Reporting.Features.ExportLeaveCalendarReport;
using HR.Modules.Reporting.Features.ExportLeaveSummaryReport;
using HR.Modules.Reporting.Features.ExportOnboardingProgressReport;
using HR.Modules.Reporting.Features.ExportOffboardingProgressReport;
using HR.Modules.Reporting.Features.ExportDocumentComplianceReport;
using HR.Modules.Reporting.Features.ExportCompanyDocumentAcknowledgementReport;
using HR.Modules.Reporting.Features.ExportProbationReport;
using HR.Modules.Reporting.Features.ExportRecruitmentPipelineReport;
using HR.Modules.Reporting.Features.ExportRecruitmentPipelineSummaryReport;
using HR.Modules.Reporting.Features.ExportSicknessReport;
using HR.Modules.Reporting.Features.ExportVacancyPerformanceReport;
using HR.Modules.Reporting.Features.GetAssetAssignmentReport;
using HR.Modules.Reporting.Features.GetEmployeeDirectoryReport;
using HR.Modules.Reporting.Features.GetEmployeeLeaverReport;
using HR.Modules.Reporting.Features.GetEmployeeStarterReport;
using HR.Modules.Reporting.Features.GetHrHeadcountSummaryReport;
using HR.Modules.Reporting.Features.GetLeaveCalendarReport;
using HR.Modules.Reporting.Features.GetLeaveSummaryReport;
using HR.Modules.Reporting.Features.GetOnboardingProgressReport;
using HR.Modules.Reporting.Features.GetOffboardingProgressReport;
using HR.Modules.Reporting.Features.GetDocumentComplianceReport;
using HR.Modules.Reporting.Features.GetCompanyDocumentAcknowledgementReport;
using HR.Modules.Reporting.Features.GetProbationReport;
using HR.Modules.Reporting.Features.GetRecruitmentPipelineReport;
using HR.Modules.Reporting.Features.GetRecruitmentPipelineSummaryReport;
using HR.Modules.Reporting.Features.GetReportCatalog;
using HR.Modules.Reporting.Features.GetReportFavourites;
using HR.Modules.Reporting.Features.GetReportViews;
using HR.Modules.Reporting.Features.GetComplianceCentre;
using HR.Modules.Reporting.Features.GetGovernanceUserActivityReport;
using HR.Modules.Reporting.Features.GetGovernanceAdministrativeChangesReport;
using HR.Modules.Reporting.Features.GetGovernanceSecurityEventsReport;
using HR.Modules.Reporting.Features.GetGovernanceComplianceStatusReport;
using HR.Modules.Reporting.Features.ExportGovernanceUserActivityReport;
using HR.Modules.Reporting.Features.ExportGovernanceAdministrativeChangesReport;
using HR.Modules.Reporting.Features.ExportGovernanceSecurityEventsReport;
using HR.Modules.Reporting.Features.ExportGovernanceComplianceStatusReport;
using HR.Modules.Reporting.Jobs;
using HR.Modules.Reporting.Features.GetSicknessReport;
using HR.Modules.Reporting.Features.GetVacancyPerformanceReport;
using HR.Modules.Reporting.Features.GetWorkloadActions;
using HR.Modules.Reporting.Features.ExportWorkloadActions;
using HR.Modules.Reporting.Features.RemoveReportFavourite;
using HR.Modules.Reporting.Features.RenameReportView;
using HR.Modules.Reporting.Features.SaveReportView;
using HR.Modules.Reporting.Features.SetDefaultReportView;
using HR.Modules.Reporting.Persistence;
using HR.Modules.Reporting.Services;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Reporting;

public static class ReportingModule
{
    public static IServiceCollection AddReportingModule(
        this IServiceCollection services,
        string connectionString)
    {
        AddFeatureServices(services);

        services.AddDbContext<ReportingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "reporting")));

        return services;
    }

    /// <summary>
    /// ADM-03: daily scan that raises Compliance-category administrative alerts for overdue
    /// compliance items across every active company. Runs after the document/probation reminder
    /// jobs (Cron.Daily(12)) so the compliance readers reflect that day's reminder processing.
    /// </summary>
    public static WebApplication UseReportingRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        jobManager.AddOrUpdate<GenerateComplianceAlertsJob>(
            "reporting-compliance-alerts",
            job => job.ExecuteAsync(),
            Cron.Daily(12));
        return app;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        // ADM-03: daily compliance-alert scan.
        services.AddScoped<GenerateComplianceAlertsJob>();

        // REP-06: shared export auditing helper used by every Export*Report handler.
        services.AddScoped<ReportExportAuditor>();

        services.AddScoped<GetReportCatalogHandler>();
        services.AddScoped<IValidator<GetReportCatalogRequest>, GetReportCatalogValidator>();

        services.AddScoped<GetEmployeeDirectoryReportHandler>();
        services.AddScoped<IValidator<GetEmployeeDirectoryReportRequest>, GetEmployeeDirectoryReportValidator>();

        services.AddScoped<ExportEmployeeDirectoryReportHandler>();
        services.AddScoped<IValidator<ExportEmployeeDirectoryReportRequest>, ExportEmployeeDirectoryReportValidator>();

        services.AddScoped<GetHrHeadcountSummaryReportHandler>();
        services.AddScoped<IValidator<GetHrHeadcountSummaryReportRequest>, GetHrHeadcountSummaryReportValidator>();

        services.AddScoped<ExportHrHeadcountSummaryReportHandler>();
        services.AddScoped<IValidator<ExportHrHeadcountSummaryReportRequest>, ExportHrHeadcountSummaryReportValidator>();

        services.AddScoped<GetEmployeeStarterReportHandler>();
        services.AddScoped<IValidator<GetEmployeeStarterReportRequest>, GetEmployeeStarterReportValidator>();

        services.AddScoped<ExportEmployeeStarterReportHandler>();
        services.AddScoped<IValidator<ExportEmployeeStarterReportRequest>, ExportEmployeeStarterReportValidator>();

        services.AddScoped<GetEmployeeLeaverReportHandler>();
        services.AddScoped<IValidator<GetEmployeeLeaverReportRequest>, GetEmployeeLeaverReportValidator>();

        services.AddScoped<ExportEmployeeLeaverReportHandler>();
        services.AddScoped<IValidator<ExportEmployeeLeaverReportRequest>, ExportEmployeeLeaverReportValidator>();

        services.AddScoped<GetLeaveSummaryReportHandler>();
        services.AddScoped<IValidator<GetLeaveSummaryReportRequest>, GetLeaveSummaryReportValidator>();

        services.AddScoped<ExportLeaveSummaryReportHandler>();
        services.AddScoped<IValidator<ExportLeaveSummaryReportRequest>, ExportLeaveSummaryReportValidator>();

        services.AddScoped<GetLeaveCalendarReportHandler>();
        services.AddScoped<IValidator<GetLeaveCalendarReportRequest>, GetLeaveCalendarReportValidator>();

        services.AddScoped<ExportLeaveCalendarReportHandler>();
        services.AddScoped<IValidator<ExportLeaveCalendarReportRequest>, ExportLeaveCalendarReportValidator>();

        services.AddScoped<GetReportFavouritesHandler>();
        services.AddScoped<IValidator<GetReportFavouritesRequest>, GetReportFavouritesValidator>();

        services.AddScoped<AddReportFavouriteHandler>();
        services.AddScoped<IValidator<AddReportFavouriteRequest>, AddReportFavouriteValidator>();

        services.AddScoped<RemoveReportFavouriteHandler>();
        services.AddScoped<IValidator<RemoveReportFavouriteRequest>, RemoveReportFavouriteValidator>();

        services.AddScoped<SaveReportViewHandler>();
        services.AddScoped<IValidator<SaveReportViewRequest>, SaveReportViewValidator>();

        services.AddScoped<RenameReportViewHandler>();
        services.AddScoped<IValidator<RenameReportViewRequest>, RenameReportViewValidator>();

        services.AddScoped<DeleteReportViewHandler>();
        services.AddScoped<IValidator<DeleteReportViewRequest>, DeleteReportViewValidator>();

        services.AddScoped<SetDefaultReportViewHandler>();
        services.AddScoped<IValidator<SetDefaultReportViewRequest>, SetDefaultReportViewValidator>();

        services.AddScoped<GetReportViewsHandler>();
        services.AddScoped<IValidator<GetReportViewsRequest>, GetReportViewsValidator>();

        services.AddScoped<GetSicknessReportHandler>();
        services.AddScoped<IValidator<GetSicknessReportRequest>, GetSicknessReportValidator>();

        services.AddScoped<ExportSicknessReportHandler>();
        services.AddScoped<IValidator<ExportSicknessReportRequest>, ExportSicknessReportValidator>();

        services.AddScoped<GetRecruitmentPipelineReportHandler>();
        services.AddScoped<IValidator<GetRecruitmentPipelineReportRequest>, GetRecruitmentPipelineReportValidator>();

        services.AddScoped<ExportRecruitmentPipelineReportHandler>();
        services.AddScoped<IValidator<ExportRecruitmentPipelineReportRequest>, ExportRecruitmentPipelineReportValidator>();

        services.AddScoped<GetRecruitmentPipelineSummaryReportHandler>();
        services.AddScoped<IValidator<GetRecruitmentPipelineSummaryReportRequest>, GetRecruitmentPipelineSummaryReportValidator>();

        services.AddScoped<ExportRecruitmentPipelineSummaryReportHandler>();
        services.AddScoped<IValidator<ExportRecruitmentPipelineSummaryReportRequest>, ExportRecruitmentPipelineSummaryReportValidator>();

        services.AddScoped<GetVacancyPerformanceReportHandler>();
        services.AddScoped<IValidator<GetVacancyPerformanceReportRequest>, GetVacancyPerformanceReportValidator>();

        services.AddScoped<ExportVacancyPerformanceReportHandler>();
        services.AddScoped<IValidator<ExportVacancyPerformanceReportRequest>, ExportVacancyPerformanceReportValidator>();

        services.AddScoped<GetProbationReportHandler>();
        services.AddScoped<IValidator<GetProbationReportRequest>, GetProbationReportValidator>();

        services.AddScoped<ExportProbationReportHandler>();
        services.AddScoped<IValidator<ExportProbationReportRequest>, ExportProbationReportValidator>();

        services.AddScoped<GetOnboardingProgressReportHandler>();
        services.AddScoped<IValidator<GetOnboardingProgressReportRequest>, GetOnboardingProgressReportValidator>();

        services.AddScoped<ExportOnboardingProgressReportHandler>();
        services.AddScoped<IValidator<ExportOnboardingProgressReportRequest>, ExportOnboardingProgressReportValidator>();

        services.AddScoped<GetOffboardingProgressReportHandler>();
        services.AddScoped<IValidator<GetOffboardingProgressReportRequest>, GetOffboardingProgressReportValidator>();

        services.AddScoped<ExportOffboardingProgressReportHandler>();
        services.AddScoped<IValidator<ExportOffboardingProgressReportRequest>, ExportOffboardingProgressReportValidator>();

        services.AddScoped<GetDocumentComplianceReportHandler>();
        services.AddScoped<IValidator<GetDocumentComplianceReportRequest>, GetDocumentComplianceReportValidator>();

        services.AddScoped<ExportDocumentComplianceReportHandler>();
        services.AddScoped<IValidator<ExportDocumentComplianceReportRequest>, ExportDocumentComplianceReportValidator>();

        services.AddScoped<GetCompanyDocumentAcknowledgementReportHandler>();
        services.AddScoped<IValidator<GetCompanyDocumentAcknowledgementReportRequest>, GetCompanyDocumentAcknowledgementReportValidator>();

        services.AddScoped<ExportCompanyDocumentAcknowledgementReportHandler>();
        services.AddScoped<IValidator<ExportCompanyDocumentAcknowledgementReportRequest>, ExportCompanyDocumentAcknowledgementReportValidator>();

        services.AddScoped<GetAssetAssignmentReportHandler>();
        services.AddScoped<IValidator<GetAssetAssignmentReportRequest>, GetAssetAssignmentReportValidator>();

        services.AddScoped<ExportAssetAssignmentReportHandler>();
        services.AddScoped<IValidator<ExportAssetAssignmentReportRequest>, ExportAssetAssignmentReportValidator>();

        // OBT-721 Workload & HR Actions Report — aggregates all registered IWorkloadActionProvider
        // implementations from other modules (registered against the shared interface in
        // HR.Infrastructure.Abstractions from each owning module's own ModuleRegistration).
        services.AddScoped<GetWorkloadActionsHandler>();
        services.AddScoped<IValidator<GetWorkloadActionsRequest>, GetWorkloadActionsValidator>();

        services.AddScoped<ExportWorkloadActionsHandler>();
        services.AddScoped<IValidator<ExportWorkloadActionsRequest>, ExportWorkloadActionsValidator>();

        services.AddScoped<GetComplianceCentreHandler>();
        services.AddScoped<IValidator<GetComplianceCentreRequest>, GetComplianceCentreValidator>();

        // ADM-08: administrative governance reporting hub. The three audit-backed reports read the
        // central audit source via IAuditHistoryReader; the compliance-status report delegates to
        // the ADM-02 GetComplianceCentreHandler registered above.
        services.AddScoped<GetGovernanceUserActivityReportHandler>();
        services.AddScoped<IValidator<GetGovernanceUserActivityReportRequest>, GetGovernanceUserActivityReportValidator>();
        services.AddScoped<ExportGovernanceUserActivityReportHandler>();
        services.AddScoped<IValidator<ExportGovernanceUserActivityReportRequest>, ExportGovernanceUserActivityReportValidator>();

        services.AddScoped<GetGovernanceAdministrativeChangesReportHandler>();
        services.AddScoped<IValidator<GetGovernanceAdministrativeChangesReportRequest>, GetGovernanceAdministrativeChangesReportValidator>();
        services.AddScoped<ExportGovernanceAdministrativeChangesReportHandler>();
        services.AddScoped<IValidator<ExportGovernanceAdministrativeChangesReportRequest>, ExportGovernanceAdministrativeChangesReportValidator>();

        services.AddScoped<GetGovernanceSecurityEventsReportHandler>();
        services.AddScoped<IValidator<GetGovernanceSecurityEventsReportRequest>, GetGovernanceSecurityEventsReportValidator>();
        services.AddScoped<ExportGovernanceSecurityEventsReportHandler>();
        services.AddScoped<IValidator<ExportGovernanceSecurityEventsReportRequest>, ExportGovernanceSecurityEventsReportValidator>();

        services.AddScoped<GetGovernanceComplianceStatusReportHandler>();
        services.AddScoped<IValidator<GetGovernanceComplianceStatusReportRequest>, GetGovernanceComplianceStatusReportValidator>();
        services.AddScoped<ExportGovernanceComplianceStatusReportHandler>();
        services.AddScoped<IValidator<ExportGovernanceComplianceStatusReportRequest>, ExportGovernanceComplianceStatusReportValidator>();
    }

    public static async Task MigrateReportingAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS reporting");
        await db.Database.MigrateAsync();
    }
}
