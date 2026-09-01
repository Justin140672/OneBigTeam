using FluentValidation;
using Hangfire;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.AddMyEmergencyContact;
using HR.Modules.Employees.Features.AssignManager;
using HR.Modules.Employees.Features.GetMyContactDetails;
using HR.Modules.Employees.Features.GetMyEmergencyContacts;
using HR.Modules.Employees.Features.GetEmployeeEmergencyContacts;
using HR.Modules.Employees.Features.GetCurrentCompensation;
using HR.Modules.Employees.Features.GetCompensationHistory;
using HR.Modules.Employees.Features.GetEmployeeAuditHistory;
using HR.Modules.Employees.Features.GetRecentEmployeeChanges;
using HR.Modules.Employees.Features.CreateCompensationRecord;
using HR.Modules.Employees.Features.CreateEmployeeNote;
using HR.Modules.Employees.Features.SupersedeEmployeeNote;
using HR.Modules.Employees.Features.GetEmployeeNotes;
using HR.Modules.Employees.Features.UpdateFutureCompensationRecord;
using HR.Modules.Employees.Features.DeleteFutureCompensationRecord;
using HR.Modules.Employees.Features.BulkApplyCompensationAdjustments;
using HR.Modules.Employees.Features.GetCompensationImportTemplate;
using HR.Modules.Employees.Features.ImportCompensationChanges;
using HR.Modules.Employees.Features.ListNationalities;
using HR.Modules.Employees.Features.RemoveMyEmergencyContact;
using HR.Modules.Employees.Features.UpdateMyContactDetails;
using HR.Modules.Employees.Features.UpdateMyEmergencyContact;
using HR.Modules.Employees.Features.CreateDepartment;
using HR.Modules.Employees.Features.CreateEmployee;
using HR.Modules.Employees.Features.CreatePositionProfile;
using HR.Modules.Employees.Features.DeactivateDepartment;
using HR.Modules.Employees.Features.DeactivatePositionProfile;
using HR.Modules.Employees.Features.GetDepartment;
using HR.Modules.Employees.Features.GetEmployee;
using HR.Modules.Employees.Features.GetEmploymentTypeSplit;
using HR.Modules.Employees.Features.GetGenderSplit;
using HR.Modules.Employees.Features.GetHeadcountSummary;
using HR.Modules.Employees.Features.GetMyEmployee;
using HR.Modules.Employees.Features.GetMyTeam;
using HR.Modules.Employees.Features.GetNewHiresTrend;
using HR.Modules.Employees.Features.GetMyPersonalDetails;
using HR.Modules.Employees.Features.RequestPersonalDetailsChange;
using HR.Modules.Employees.Features.ListDepartments;
using HR.Modules.Employees.Features.ListEmployees;
using HR.Modules.Employees.Features.GetOrganisationChart;
using HR.Modules.Employees.Features.SetEmployeeWorkingPattern;
using HR.Modules.Employees.Features.UpdateDepartment;
using HR.Modules.Employees.Features.GetPositionProfile;
using HR.Modules.Employees.Features.ListPositionProfiles;
using HR.Modules.Employees.Features.UpdateEmployeeProfile;
using HR.Modules.Employees.Features.CompleteInitialEmployeeSetup;
using HR.Modules.Employees.Features.UpdateEmploymentDetails;
using HR.Modules.Employees.Features.AddRequiredDocumentToPositionProfile;
using HR.Modules.Employees.Features.ListRequiredDocumentsForPositionProfile;
using HR.Modules.Employees.Features.RemoveRequiredDocumentFromPositionProfile;
using HR.Modules.Employees.Features.AddRequiredAssetToPositionProfile;
using HR.Modules.Employees.Features.ListRequiredAssetsForPositionProfile;
using HR.Modules.Employees.Features.RemoveRequiredAssetFromPositionProfile;
using HR.Modules.Employees.Features.AddOnboardingTemplateToPositionProfile;
using HR.Modules.Employees.Features.ListOnboardingTemplatesForPositionProfile;
using HR.Modules.Employees.Features.RemoveOnboardingTemplateFromPositionProfile;
using HR.Modules.Employees.Features.UpdatePositionProfile;
using HR.Modules.Employees.Features.ListEmploymentTypes;
using HR.Modules.Employees.Features.CreateEmploymentType;
using HR.Modules.Employees.Features.UpdateEmploymentType;
using HR.Modules.Employees.Features.DeactivateEmploymentType;
using HR.Modules.Employees.Features.CreateOnboardingTemplate;
using HR.Modules.Employees.Features.GetOnboardingTemplate;
using HR.Modules.Employees.Features.ListOnboardingTemplates;
using HR.Modules.Employees.Features.UpdateOnboardingTemplate;
using HR.Modules.Employees.Features.DeactivateOnboardingTemplate;
using HR.Modules.Employees.Features.CreateLocationType;
using HR.Modules.Employees.Features.UpdateLocationType;
using HR.Modules.Employees.Features.DeactivateLocationType;
using HR.Modules.Employees.Features.ListLocationTypes;
using HR.Modules.Employees.Features.CreateLocation;
using HR.Modules.Employees.Features.UpdateLocation;
using HR.Modules.Employees.Features.DeactivateLocation;
using HR.Modules.Employees.Features.GetLocation;
using HR.Modules.Employees.Features.ListLocations;
using HR.Modules.Employees.Features.StartLeavingProcess;
using HR.Modules.Employees.Features.GetLeavingProcess;
using HR.Modules.Employees.Features.AmendLeavingProcess;
using HR.Modules.Employees.Features.CancelLeavingProcess;
using HR.Modules.Employees.Features.PromoteEmployee;
using HR.Modules.Employees.Features.GetEmployeePromotionHistory;
using HR.Modules.Employees.Features.GetEmployeeTimeline;
using HR.Modules.Employees.Features.CreateTimelineEntryOnEmployeeCreated;
using HR.Modules.Employees.Features.CreateTimelineEntryOnEmployeePromoted;
using HR.Modules.Employees.Features.CreateTimelineEntryOnManagerChanged;
using HR.Modules.Employees.Features.CreateTimelineEntryOnLocationChanged;
using HR.Modules.Employees.Features.CreateTimelineEntryOnPositionChanged;
using HR.Modules.Employees.Features.CreateTimelineEntryOnCompensationChanged;
using HR.Modules.Employees.Features.CreateTimelineEntryOnOnboardingCompleted;
using HR.Modules.Employees.Features.CreateTimelineEntryOnProbationPassed;
using HR.Modules.Employees.Features.CreateTimelineEntryOnSharedCompanyDocumentAcknowledged;
using HR.Modules.Employees.Features.CreateTimelineEntryOnEmployeeDocumentUploaded;
using HR.Modules.Employees.Features.CreateTimelineEntryOnOffboardingStarted;
using HR.Modules.Employees.Features.BackfillEmployeeTimeline;
using HR.Modules.Employees.Jobs;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Services.OnboardingTasks;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Employees;

public static class EmployeesModule
{
    public static IServiceCollection AddEmployeesModule(
        this IServiceCollection services,
        string connectionString)
    {
        AddFeatureServices(services);

        services.AddDbContext<EmployeesDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "employees")));

        return services;
    }

    public static WebApplication UseEmployeesRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        jobManager.AddOrUpdate<ProcessLeavingEmployeesJob>(
            "process-leaving-employees",
            job => job.ExecuteAsync(),
            Cron.Daily(0));
        jobManager.AddOrUpdate<ProcessPromotionsJob>(
            "process-promotions",
            job => job.ExecuteAsync(),
            Cron.Daily(0));
        return app;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<CreateDepartmentHandler>();
        services.AddScoped<IValidator<CreateDepartmentRequest>, CreateDepartmentValidator>();

        services.AddScoped<UpdateDepartmentHandler>();
        services.AddScoped<IValidator<UpdateDepartmentRequest>, UpdateDepartmentValidator>();

        services.AddScoped<DeactivateDepartmentHandler>();
        services.AddScoped<DeactivatePositionProfileHandler>();

        services.AddScoped<CreatePositionProfileHandler>();
        services.AddScoped<IValidator<CreatePositionProfileRequest>, CreatePositionProfileValidator>();

        services.AddScoped<GetPositionProfileHandler>();

        services.AddScoped<ListPositionProfilesHandler>();
        services.AddScoped<IValidator<ListPositionProfilesRequest>, ListPositionProfilesValidator>();

        services.AddScoped<UpdatePositionProfileHandler>();
        services.AddScoped<IValidator<UpdatePositionProfileRequest>, UpdatePositionProfileValidator>();

        services.AddScoped<AddRequiredDocumentHandler>();
        services.AddScoped<IValidator<AddRequiredDocumentRequest>, AddRequiredDocumentValidator>();

        services.AddScoped<RemoveRequiredDocumentHandler>();

        services.AddScoped<ListRequiredDocumentsHandler>();

        services.AddScoped<AddRequiredAssetHandler>();
        services.AddScoped<IValidator<AddRequiredAssetRequest>, AddRequiredAssetValidator>();

        services.AddScoped<RemoveRequiredAssetHandler>();

        services.AddScoped<ListRequiredAssetsHandler>();

        services.AddScoped<AddOnboardingTemplateHandler>();
        services.AddScoped<IValidator<AddOnboardingTemplateRequest>, AddOnboardingTemplateValidator>();

        services.AddScoped<RemoveOnboardingTemplateHandler>();

        services.AddScoped<ListOnboardingTemplatesForPositionProfileHandler>();
        services.AddScoped<IValidator<ListOnboardingTemplatesForPositionProfileRequest>, ListOnboardingTemplatesForPositionProfileValidator>();

        services.AddScoped<CreateEmployeeHandler>();
        services.AddScoped<IValidator<CreateEmployeeRequest>, CreateEmployeeValidator>();
        // ICompanyEmployeeNumberSettingsReader and IEmployeeNumberGenerator are registered by
        // CompaniesModule (they are implemented in HR.Modules.Companies, the owning module for
        // company settings); Employees only depends on the Infrastructure.Abstractions interfaces.

        services.AddScoped<IEmployeeRenumberingService, EmployeeRenumberingService>();

        services.AddScoped<GetEmployeeHandler>();
        services.AddScoped<GetMyEmployeeHandler>();
        services.AddScoped<GetMyTeamHandler>();
        services.AddScoped<Features.GetManagerTeamStatusSummary.GetManagerTeamStatusSummaryHandler>();
        services.AddScoped<IValidator<Features.GetManagerTeamStatusSummary.GetManagerTeamStatusSummaryRequest>,
            Features.GetManagerTeamStatusSummary.GetManagerTeamStatusSummaryValidator>();
        services.AddScoped<GetMyPersonalDetailsHandler>();
        services.AddScoped<RequestPersonalDetailsChangeHandler>();
        services.AddScoped<IValidator<RequestPersonalDetailsChangeRequest>, RequestPersonalDetailsChangeValidator>();

        services.AddScoped<ListDepartmentsHandler>();
        services.AddScoped<GetDepartmentHandler>();
        services.AddScoped<IValidator<ListDepartmentsRequest>, ListDepartmentsValidator>();

        services.AddScoped<ListEmployeesHandler>();
        services.AddScoped<IValidator<ListEmployeesRequest>, ListEmployeesValidator>();

        services.AddScoped<GetOrganisationChartHandler>();

        services.AddScoped<GetHeadcountSummaryHandler>();
        services.AddScoped<IValidator<GetHeadcountSummaryRequest>, GetHeadcountSummaryValidator>();

        services.AddScoped<GetNewHiresTrendHandler>();
        services.AddScoped<IValidator<GetNewHiresTrendRequest>, GetNewHiresTrendValidator>();

        services.AddScoped<GetGenderSplitHandler>();
        services.AddScoped<IValidator<GetGenderSplitRequest>, GetGenderSplitValidator>();

        services.AddScoped<GetEmploymentTypeSplitHandler>();
        services.AddScoped<IValidator<GetEmploymentTypeSplitRequest>, GetEmploymentTypeSplitValidator>();

        services.AddScoped<UpdateEmployeeProfileHandler>();
        services.AddScoped<IValidator<UpdateEmployeeProfileRequest>, UpdateEmployeeProfileValidator>();

        services.AddScoped<CompleteInitialEmployeeSetupHandler>();
        services.AddScoped<IValidator<CompleteInitialEmployeeSetupRequest>, CompleteInitialEmployeeSetupValidator>();

        services.AddScoped<UpdateEmploymentDetailsHandler>();
        services.AddScoped<IValidator<UpdateEmploymentDetailsRequest>, UpdateEmploymentDetailsValidator>();

        services.AddScoped<AssignManagerHandler>();
        services.AddScoped<IValidator<AssignManagerRequest>, AssignManagerValidator>();

        services.AddScoped<SetEmployeeWorkingPatternHandler>();
        services.AddScoped<IValidator<SetEmployeeWorkingPatternRequest>, SetEmployeeWorkingPatternValidator>();

        services.AddScoped<ListNationalitiesHandler>();

        services.AddScoped<GetMyContactDetailsHandler>();
        services.AddScoped<UpdateMyContactDetailsHandler>();
        services.AddScoped<IValidator<UpdateMyContactDetailsRequest>, UpdateMyContactDetailsValidator>();

        services.AddScoped<GetMyEmergencyContactsHandler>();
        services.AddScoped<AddMyEmergencyContactHandler>();
        services.AddScoped<IValidator<AddMyEmergencyContactRequest>, AddMyEmergencyContactValidator>();
        services.AddScoped<UpdateMyEmergencyContactHandler>();
        services.AddScoped<IValidator<UpdateMyEmergencyContactRequest>, UpdateMyEmergencyContactValidator>();
        services.AddScoped<RemoveMyEmergencyContactHandler>();
        services.AddScoped<GetEmployeeEmergencyContactsHandler>();

        services.AddScoped<GetCurrentCompensationHandler>();
        services.AddScoped<GetCompensationHistoryHandler>();
        services.AddScoped<GetEmployeeAuditHistoryHandler>();
        services.AddScoped<GetRecentEmployeeChangesHandler>();
        services.AddScoped<CompensationRecordWriter>();
        services.AddScoped<CreateCompensationRecordHandler>();
        services.AddScoped<IValidator<CreateCompensationRecordRequest>, CreateCompensationRecordValidator>();
        services.AddScoped<UpdateFutureCompensationRecordHandler>();
        services.AddScoped<IValidator<UpdateFutureCompensationRecordRequest>, UpdateFutureCompensationRecordValidator>();
        services.AddScoped<DeleteFutureCompensationRecordHandler>();
        services.AddScoped<BulkApplyCompensationAdjustmentsHandler>();
        services.AddScoped<IValidator<BulkApplyCompensationAdjustmentsRequest>, BulkApplyCompensationAdjustmentsValidator>();
        services.AddScoped<GetCompensationImportTemplateHandler>();
        services.AddScoped<ImportCompensationChangesHandler>();
        services.AddScoped<IValidator<ImportCompensationChangesRequest>, ImportCompensationChangesValidator>();

        services.AddScoped<CreateEmployeeNoteHandler>();
        services.AddScoped<IValidator<CreateEmployeeNoteRequest>, CreateEmployeeNoteValidator>();
        services.AddScoped<SupersedeEmployeeNoteHandler>();
        services.AddScoped<IValidator<SupersedeEmployeeNoteRequest>, SupersedeEmployeeNoteValidator>();
        services.AddScoped<GetEmployeeNotesHandler>();

        services.AddScoped<ListEmploymentTypesHandler>();
        services.AddScoped<IValidator<ListEmploymentTypesRequest>, ListEmploymentTypesValidator>();

        services.AddScoped<CreateEmploymentTypeHandler>();
        services.AddScoped<IValidator<CreateEmploymentTypeRequest>, CreateEmploymentTypeValidator>();

        services.AddScoped<UpdateEmploymentTypeHandler>();
        services.AddScoped<IValidator<UpdateEmploymentTypeRequest>, UpdateEmploymentTypeValidator>();

        services.AddScoped<DeactivateEmploymentTypeHandler>();
        services.AddScoped<IValidator<DeactivateEmploymentTypeRequest>, DeactivateEmploymentTypeValidator>();

        services.AddScoped<CreateOnboardingTemplateHandler>();
        services.AddScoped<IValidator<CreateOnboardingTemplateRequest>, CreateOnboardingTemplateValidator>();

        services.AddScoped<GetOnboardingTemplateHandler>();

        services.AddScoped<OnboardingTemplateSeeder>();
        services.AddScoped<ListOnboardingTemplatesHandler>();
        services.AddScoped<IValidator<ListOnboardingTemplatesRequest>, ListOnboardingTemplatesValidator>();

        services.AddScoped<UpdateOnboardingTemplateHandler>();
        services.AddScoped<IValidator<UpdateOnboardingTemplateRequest>, UpdateOnboardingTemplateValidator>();

        services.AddScoped<DeactivateOnboardingTemplateHandler>();

        services.AddScoped<CreateLocationTypeHandler>();
        services.AddScoped<IValidator<CreateLocationTypeRequest>, CreateLocationTypeValidator>();

        services.AddScoped<UpdateLocationTypeHandler>();
        services.AddScoped<IValidator<UpdateLocationTypeRequest>, UpdateLocationTypeValidator>();

        services.AddScoped<DeactivateLocationTypeHandler>();
        services.AddScoped<IValidator<DeactivateLocationTypeRequest>, DeactivateLocationTypeValidator>();

        services.AddScoped<ListLocationTypesHandler>();
        services.AddScoped<IValidator<ListLocationTypesRequest>, ListLocationTypesValidator>();

        services.AddScoped<CreateLocationHandler>();
        services.AddScoped<IValidator<CreateLocationRequest>, CreateLocationValidator>();

        services.AddScoped<UpdateLocationHandler>();
        services.AddScoped<IValidator<UpdateLocationRequest>, UpdateLocationValidator>();

        services.AddScoped<DeactivateLocationHandler>();
        services.AddScoped<IValidator<DeactivateLocationRequest>, DeactivateLocationValidator>();

        services.AddScoped<GetLocationHandler>();
        services.AddScoped<IValidator<GetLocationRequest>, GetLocationValidator>();

        services.AddScoped<ListLocationsHandler>();
        services.AddScoped<IValidator<ListLocationsRequest>, ListLocationsValidator>();

        services.AddScoped<StartLeavingProcessHandler>();
        services.AddScoped<IValidator<StartLeavingProcessRequest>, StartLeavingProcessValidator>();

        services.AddScoped<GetLeavingProcessHandler>();

        services.AddScoped<AmendLeavingProcessHandler>();
        services.AddScoped<IValidator<AmendLeavingProcessRequest>, AmendLeavingProcessValidator>();

        services.AddScoped<CancelLeavingProcessHandler>();
        services.AddScoped<IValidator<CancelLeavingProcessRequest>, CancelLeavingProcessValidator>();

        services.AddScoped<ProcessLeavingEmployeesJob>();

        services.AddScoped<PromoteEmployeeHandler>();
        services.AddScoped<IValidator<PromoteEmployeeRequest>, PromoteEmployeeValidator>();

        services.AddScoped<GetEmployeePromotionHistoryHandler>();

        services.AddScoped<ProcessPromotionsJob>();

        services.AddScoped<GetEmployeeTimelineHandler>();
        services.AddScoped<IValidator<GetEmployeeTimelineRequest>, GetEmployeeTimelineValidator>();
        services.AddScoped<IEmployeeTimelineWriter, EmployeeTimelineWriter>();

        // Wave 2a: cross-module timeline-populating integration event handlers. All handlers live
        // in Employees regardless of which module publishes the event (see architecture note in
        // GetEmployeeTimeline/EmployeeTimelineWriter).
        services.AddScoped<IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>, EmployeeCreatedHandler>();
        services.AddScoped<IIntegrationEventHandler<EmployeePromotedIntegrationEvent>, EmployeePromotedHandler>();
        services.AddScoped<IIntegrationEventHandler<EmployeeManagerChangedIntegrationEvent>, ManagerChangedHandler>();
        services.AddScoped<IIntegrationEventHandler<EmployeeLocationChangedIntegrationEvent>, LocationChangedHandler>();
        services.AddScoped<IIntegrationEventHandler<EmployeePositionChangedIntegrationEvent>, PositionChangedHandler>();
        services.AddScoped<IIntegrationEventHandler<CompensationChangedIntegrationEvent>, CompensationChangedHandler>();
        services.AddScoped<IIntegrationEventHandler<OnboardingCompletedIntegrationEvent>, OnboardingCompletedHandler>();
        services.AddScoped<IIntegrationEventHandler<ProbationPassedIntegrationEvent>, ProbationPassedHandler>();
        services.AddScoped<IIntegrationEventHandler<ProbationFailedIntegrationEvent>, HR.Modules.Employees.Features.CreateTimelineEntryOnProbationFailed.ProbationFailedHandler>();
        services.AddScoped<IIntegrationEventHandler<ProbationExtendedIntegrationEvent>, HR.Modules.Employees.Features.CreateTimelineEntryOnProbationExtended.ProbationExtendedHandler>();
        services.AddScoped<IIntegrationEventHandler<SharedCompanyDocumentAcknowledgedIntegrationEvent>, SharedCompanyDocumentAcknowledgedHandler>();
        services.AddScoped<IIntegrationEventHandler<EmployeeDocumentUploadedIntegrationEvent>, EmployeeDocumentUploadedHandler>();
        services.AddScoped<IIntegrationEventHandler<OffboardingStartedIntegrationEvent>, OffboardingStartedHandler>();

        services.AddScoped<BackfillEmployeeTimelineHandler>();
        services.AddScoped<IValidator<BackfillEmployeeTimelineRequest>, BackfillEmployeeTimelineValidator>();

        services.AddScoped<IProbationDateResolver, ProbationDateResolver>();
        services.AddScoped<IEffectiveNoticePeriodResolver, EffectiveNoticePeriodResolver>();
        services.AddScoped<IEmployeeDepartureFinalizer, EmployeeDepartureFinalizer>();
        services.AddScoped<IEmployeePromotionFinalizer, EmployeePromotionFinalizer>();
        services.AddScoped<IWorkingPatternProvider, WorkingPatternProvider>();
        services.AddScoped<IDirectReportsReader, DirectReportsReader>();
        services.AddScoped<IEmployeeDirectoryReader, EmployeeDirectoryReader>();
        services.AddScoped<IHrHeadcountSummaryReader, HrHeadcountSummaryReader>();
        services.AddScoped<IEmployeeDepartmentReader, EmployeeDepartmentReader>();
        services.AddScoped<IEmployeeDataExportSource, EmployeeDataExportSource>();
        services.AddScoped<IEmployeeStarterReader, EmployeeStarterReader>();
        services.AddScoped<IEmployeeLeaverReader, EmployeeLeaverReader>();
        services.AddScoped<IEmployeeNameReader, EmployeeNameReader>();
        services.AddScoped<IEmployeeAudienceReader, EmployeeAudienceReader>();
        services.AddScoped<IEmployeeInviteCandidateReader, EmployeeInviteCandidateReader>();
        services.AddScoped<IManagerReader, ManagerReader>();
        services.AddScoped<IActiveLeavingProcessReader, ActiveLeavingProcessReader>();
        services.AddScoped<IEmployeeStartDateReader, EmployeeStartDateReader>();
        services.AddScoped<IEmployeeProbationDatesReader, EmployeeProbationDatesReader>();
        services.AddScoped<IPositionProfileDocumentsReader, PositionProfileDocumentsReader>();
        services.AddScoped<IPositionProfileAssetsReader, PositionProfileAssetsReader>();
        services.AddScoped<IPositionProfileReader, PositionProfileReader>();
        services.AddScoped<ICurrentEmployeeReader, CurrentEmployeeReader>();
        services.AddScoped<IOnboardingTemplateReader, OnboardingTemplateReader>();
        services.AddScoped<IEmployeeProvisioningService, EmployeeProvisioningService>();
        services.AddScoped<ICompanyDefaultDataSeeder, CompanyDefaultDataSeeder>();
        services.AddScoped<IEmployeeImportLookupReader, EmployeeImportLookupReader>();
        services.AddScoped<IImportLookupResolver, ImportLookupResolver>();
        services.AddScoped<IEmployeeImportWriter, EmployeeImportWriter>();
        services.AddScoped<WorkingPatternCompensationCalculator>();
        services.AddScoped<IWorkloadActionProvider, UpcomingEmployeeStartDatesWorkloadActionProvider>();
        services.AddScoped<IWorkloadActionProvider, UpcomingEmployeeLeavingDatesWorkloadActionProvider>();

        // Getting Started checklist task definitions (HR.Modules.CompanyOnboarding epic, Phase A).
        services.AddScoped<IOnboardingTaskDefinition, DownloadEmployeeImportTemplateTask>();
        services.AddScoped<IOnboardingTaskDefinition, ImportEmployeesTask>();
        services.AddScoped<IOnboardingTaskDefinition, CompleteEmployeeRecordTask>();
    }

    public static async Task MigrateEmployeesAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS employees");
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// The deterministic Acme "arrange-data" employees seeded only for the Playwright E2E run
    /// (see <see cref="SeedEmployeesAsync"/>'s <c>includeE2eTestPool</c> path). Exposed so the
    /// API host can also hand these ids/names to the Onboarding module's E2E plan seeder without
    /// re-declaring the GUID scheme. Mirrored verbatim in
    /// tests/HR.Web.E2E.Tests/Infrastructure/SeededE2eEmployees.cs — keep the two in sync.
    /// GUID scheme: 3E2E0000-0000-0000-0000-0000000000NN (NN = two-digit index).
    /// </summary>
    public static readonly IReadOnlyList<(int Index, Guid Id, string LastName, string Email, string EmployeeNumber, bool ManagedByDavidPark)> E2eTestPool =
        BuildE2eTestPool();

    private static (int, Guid, string, string, string, bool)[] BuildE2eTestPool()
    {
        (int Nn, string LastName, bool Mgr)[] defs =
        {
            (1, "SeedProfileView", false),
            (2, "SeedTimelineA", false), (3, "SeedTimelineB", false), (4, "SeedTimelineC", false),
            (5, "SeedLifecycleA", false), (6, "SeedLifecycleB", false),
            (7, "SeedNoticePeriodA", false), (8, "SeedNoticePeriodB", false), (9, "SeedNoticePeriodC", false),
            (10, "SeedListUiA", false), (11, "SeedListUiB", false), (12, "SeedListUiC", false),
            (13, "SeedBulkA", false), (14, "SeedBulkB", false), (15, "SeedBulkC", false), (16, "SeedBulkD", false),
            (17, "SeedBulkE", false), (18, "SeedBulkF", false), (19, "SeedBulkG", false), (20, "SeedBulkH", false),
            (21, "SeedMgrDashA", true), (22, "SeedMgrDashB", true), (23, "SeedMgrDashC", true),
            (24, "SeedAssetAck", false), (25, "SeedAssetReturn", false), (26, "SeedSelfServiceDoc", false),
            (27, "SeedLeavingA", false), (28, "SeedLeavingB", false), (29, "SeedLeavingC", false), (30, "SeedLeavingD", false),
            (31, "SeedLeavingE", false), (32, "SeedLeavingF", false), (33, "SeedLeavingG", false), (34, "SeedLeavingH", false),
            (35, "SeedOffboardTabA", false), (36, "SeedOffboardTabB", false), (37, "SeedOffboardTabC", false), (38, "SeedOffboardTabD", false),
            (39, "SeedOffboardConfA", false), (40, "SeedOffboardConfB", false), (41, "SeedOffboardConfC", false), (42, "SeedOffboardConfD", false),
            (43, "SeedOnboardTabA", false), (44, "SeedOnboardTabB", false), (45, "SeedOnboardTabC", false),
            (46, "SeedOnboardTabD", false), (47, "SeedOnboardTabE", false), (48, "SeedOnboardTabF", false),
        };

        return Array.ConvertAll(defs, d => (
            d.Nn,
            Guid.Parse($"3E2E0000-0000-0000-0000-0000000000{d.Nn:D2}"),
            d.LastName,
            $"e2e.seed{d.Nn:D2}@acme.example",
            $"E2E-SEED-{d.Nn:D2}",
            d.Mgr));
    }

    public static async Task SeedEmployeesAsync(this IServiceProvider services, bool includeE2eTestPool = false)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();

        var now = DateTimeOffset.UtcNow;

        // ── Nationalities (global reference data) ─────────────────────────────
        if (!await db.Nationalities.AnyAsync())
        {
            string[] names =
            [
                "Afghan", "Albanian", "Algerian", "American", "Argentine", "Armenian",
                "Australian", "Austrian", "Azerbaijani", "Bangladeshi", "Belgian",
                "Bolivian", "Brazilian", "British", "Bulgarian", "Cambodian", "Canadian",
                "Chilean", "Chinese", "Colombian", "Croatian", "Czech", "Danish", "Dutch",
                "Egyptian", "Ethiopian", "Filipino", "Finnish", "French", "Georgian",
                "German", "Ghanaian", "Greek", "Hungarian", "Indian", "Indonesian",
                "Iranian", "Iraqi", "Irish", "Israeli", "Italian", "Jamaican", "Japanese",
                "Jordanian", "Kenyan", "Korean", "Lebanese", "Malaysian", "Mexican",
                "Moroccan", "Nepalese", "New Zealander", "Nigerian", "Norwegian",
                "Pakistani", "Peruvian", "Polish", "Portuguese", "Romanian", "Russian",
                "Saudi Arabian", "Serbian", "Singaporean", "Somali", "South African",
                "Spanish", "Sri Lankan", "Swedish", "Swiss", "Syrian", "Taiwanese",
                "Thai", "Turkish", "Ugandan", "Ukrainian", "Vietnamese", "Zimbabwean"
            ];

            for (var i = 0; i < names.Length; i++)
                db.Nationalities.Add(Nationality.Create(i + 1, names[i]));

            await db.SaveChangesAsync();
        }

        // ── Acme Corporation ─────────────────────────────────────────────────
        var acmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        if (!await db.Employees.AnyAsync(e => e.CompanyId == acmeId))
        {
            // Seed employment types for Acme
            var etPermId      = Guid.Parse("40000000-0000-0000-0000-000000000001");
            var etFixedTermId = Guid.Parse("40000000-0000-0000-0000-000000000002");
            var etContractId  = Guid.Parse("40000000-0000-0000-0000-000000000003");
            var etCasualId    = Guid.Parse("40000000-0000-0000-0000-000000000004");
            var etApprentId   = Guid.Parse("40000000-0000-0000-0000-000000000005");

            db.EmploymentTypes.AddRange(
                EmploymentType.Create(etPermId,      acmeId, "Permanent",   null, now),
                EmploymentType.Create(etFixedTermId, acmeId, "Fixed Term",  null, now),
                EmploymentType.Create(etContractId,  acmeId, "Contractor",  null, now),
                EmploymentType.Create(etCasualId,    acmeId, "Casual",      null, now),
                EmploymentType.Create(etApprentId,   acmeId, "Apprentice",  null, now));

            var deptEngId      = Guid.Parse("10000000-0000-0000-0000-000000000001");
            var deptHrId       = Guid.Parse("10000000-0000-0000-0000-000000000002");
            var deptFinanceId  = Guid.Parse("10000000-0000-0000-0000-000000000003");
            var deptSalesId    = Guid.Parse("10000000-0000-0000-0000-000000000004");

            db.Departments.AddRange(
                Department.Create(deptEngId,     acmeId, "Engineering", "Product and platform engineering", now),
                Department.Create(deptHrId,      acmeId, "People & HR",  "HR and people operations",       now),
                Department.Create(deptFinanceId, acmeId, "Finance",       "Finance and accounting",         now),
                Department.Create(deptSalesId,   acmeId, "Sales",         "Sales and account management",   now));

            // A single seeded office location, referenced by one position profile below so
            // the "position profile defaults" cascade (Department + Location) has real data
            // to demonstrate in the UI and in E2E coverage.
            var locTypeOfficeId = Guid.Parse("60000000-0000-0000-0000-000000000001");
            var locLondonId     = Guid.Parse("70000000-0000-0000-0000-000000000001");

            // A second Location Type ("Remote") with its own Location ("Home") — so location
            // data isn't exclusively office-based, e.g. for filters/forms that need more than one
            // Location Type to demonstrate against.
            var locTypeRemoteId = Guid.Parse("60000000-0000-0000-0000-000000000002");
            var locHomeId       = Guid.Parse("70000000-0000-0000-0000-000000000002");

            db.LocationTypes.AddRange(
                LocationType.Create(locTypeOfficeId, acmeId, "Office", null, now),
                LocationType.Create(locTypeRemoteId, acmeId, "Remote", null, now));
            db.Locations.AddRange(
                Location.Create(locLondonId, acmeId, locTypeOfficeId, "London Office", null, now),
                Location.Create(locHomeId,   acmeId, locTypeRemoteId, "Home", null, now));

            // Shared with HR.Modules.Leave's seed data (LeaveModule.SeedLeaveAsync) — Employees cannot
            // reference Leave's DbContext/entities directly (no cross-module DB references), so both
            // modules seed the same hardcoded LeavePolicy id constant, matching the existing pattern
            // used for shared CompanyId constants across module seed methods.
            var acmeLeavePolicyId = Guid.Parse("C0000000-0000-0000-0000-000000000001");

            var posCtoId        = Guid.Parse("20000000-0000-0000-0000-000000000001");
            var posSenDevId     = Guid.Parse("20000000-0000-0000-0000-000000000002");
            var posDevId        = Guid.Parse("20000000-0000-0000-0000-000000000003");
            var posHrMgrId      = Guid.Parse("20000000-0000-0000-0000-000000000004");
            var posHrAdvisorId  = Guid.Parse("20000000-0000-0000-0000-000000000005");
            var posFinanceMgrId = Guid.Parse("20000000-0000-0000-0000-000000000006");
            var posSalesMgrId   = Guid.Parse("20000000-0000-0000-0000-000000000007");
            var posAeId         = Guid.Parse("20000000-0000-0000-0000-000000000008");
            // Priya Shah (see below) is a Company Administrator, not a line role in the org chart —
            // she still needs a real position/department pair like any other employee.
            var posCfoId        = Guid.Parse("20000000-0000-0000-0000-000000000009");
            // Deliberately unoccupied — no MakeAcme(...) employee below is assigned this profile.
            // Used by VacancyDetail's "New Vacancy" Position Profile dropdown, which filters to
            // profiles with no currently-active employee assigned.
            var posMarketingCoordId = Guid.Parse("20000000-0000-0000-0000-00000000000A");
            // Deliberately unoccupied at seed time, with the same Department (Engineering) and
            // Location (London Office) as "Senior Software Engineer" — dedicated to E2E tests that
            // need to create/promote an employee into a profile without permanently occupying
            // "Senior Software Engineer" itself. That profile's own vacancy (VacancyModule's dev
            // seed) is depended on by many Recruitment E2E tests' "New Vacancy" Position Profile
            // dropdown, which excludes profiles with any currently-active employee — a test that
            // creates/promotes an employee onto "Senior Software Engineer" would otherwise
            // permanently hide it from those unrelated tests for the rest of the run (see
            // CreateEmployeeTests and EmployeePromotionTabTests).
            var posQaEngId = Guid.Parse("20000000-0000-0000-0000-00000000000B");

            db.PositionProfiles.AddRange(
                PositionProfile.Create(posCtoId,        acmeId, deptEngId,     locLondonId, "Chief Technology Officer", null, null, null, null, null, null, null, acmeLeavePolicyId, now),
                PositionProfile.Create(posSenDevId,     acmeId, deptEngId,     locLondonId, "Senior Software Engineer", null, null, null, null, null, null, null, acmeLeavePolicyId, now),
                PositionProfile.Create(posDevId,        acmeId, deptEngId,     locLondonId, "Software Engineer",        null, null, null, null, null, null, null, acmeLeavePolicyId, now),
                PositionProfile.Create(posHrMgrId,      acmeId, deptHrId,      locLondonId, "HR Manager",               null, null, null, null, null, null, null, acmeLeavePolicyId, now),
                PositionProfile.Create(posHrAdvisorId,  acmeId, deptHrId,      locLondonId, "HR Advisor",               null, null, null, null, null, null, null, acmeLeavePolicyId, now),
                PositionProfile.Create(posFinanceMgrId, acmeId, deptFinanceId, locLondonId, "Finance Manager",          null, null, null, null, null, null, null, acmeLeavePolicyId, now),
                PositionProfile.Create(posSalesMgrId,   acmeId, deptSalesId,   locLondonId, "Sales Manager",            null, null, null, null, null, null, null, acmeLeavePolicyId, now),
                PositionProfile.Create(posAeId,         acmeId, deptSalesId,   locLondonId, "Account Executive",        null, null, null, null, null, null, null, acmeLeavePolicyId, now),
                PositionProfile.Create(posCfoId,        acmeId, deptFinanceId, locLondonId, "Chief Financial Officer",  null, null, null, null, null, null, null, acmeLeavePolicyId, now),
                PositionProfile.Create(posMarketingCoordId, acmeId, deptSalesId, locLondonId, "Marketing Coordinator",  null, null, null, null, null, null, null, acmeLeavePolicyId, now),
                PositionProfile.Create(posQaEngId,      acmeId, deptEngId,     locLondonId, "QA Engineer",              null, null, null, null, null, null, null, acmeLeavePolicyId, now));

            var empCtoId      = Guid.Parse("30000000-0000-0000-0000-000000000001");
            var empSenDev1Id  = Guid.Parse("30000000-0000-0000-0000-000000000002");
            var empSenDev2Id  = Guid.Parse("30000000-0000-0000-0000-000000000003");
            var empDev1Id     = Guid.Parse("30000000-0000-0000-0000-000000000004");
            var empHrMgrId    = Guid.Parse("30000000-0000-0000-0000-000000000005");
            var empHrAdvId    = Guid.Parse("30000000-0000-0000-0000-000000000006");
            var empFinMgrId   = Guid.Parse("30000000-0000-0000-0000-000000000007");
            var empSalesMgrId = Guid.Parse("30000000-0000-0000-0000-000000000008");
            var empAe1Id      = Guid.Parse("30000000-0000-0000-0000-000000000009");
            var empAe2Id      = Guid.Parse("30000000-0000-0000-0000-000000000010");
            // Must equal the ApplicationUser id IdentityModule seeds for Priya Shah (Company
            // Administrator) — GetMyEmployeeHandler resolves "my employee record" by matching the
            // signed-in user's id directly against Employee.Id, so without a real Employee row at
            // this exact id she'd have no employee record at all despite holding the Employee role.
            var empCfoId      = Guid.Parse("30000000-0000-0000-0000-000000000013");

            Employee MakeAcme(Guid id, string first, string last, string email, DateOnly start,
                              Guid deptId, Guid posId, Guid? managerId,
                              DateOnly dob, string nationality, string gender,
                              string? personalEmail, string? phone,
                              string addr1, string? addr2, string city, string? county, string postCode,
                              string employeeNumber, Guid employmentTypeId,
                              string? preferredName = null)
            {
                var e = Employee.Create(
                    id, acmeId, first, last, email, start, hasSystemAccess: true,
                    dob, nationality, gender, employeeNumber, employmentTypeId,
                    deptId, locLondonId, posId, now);
                e.Assign(deptId, posId, locLondonId, managerId, now);
                e.UpdatePersonalDetails(preferredName ?? first, dob, nationality, gender, null, now);
                e.UpdateContactDetails(personalEmail, phone, null, addr1, addr2, city, county, postCode, "United Kingdom", now);
                e.UpdateEmploymentDetails(employeeNumber, employmentTypeId, start, null, null, null, null, now);
                e.Activate(now);
                return e;
            }

            db.Employees.AddRange(
                MakeAcme(empCtoId,      "Sarah",  "Chen",     "sarah.chen@acme.example",     new DateOnly(2020, 1, 6),  deptEngId,     posCtoId,        null,         new DateOnly(1982, 3, 15),  "Taiwanese", "Female", "sarah.chen@gmail.com",       "07700 900001", "14 Rivington Street",  null,       "London",      "Greater London",     "EC2A 3DU",  "ACME-001", etPermId),
                MakeAcme(empSenDev1Id,  "James",  "Okafor",   "james.okafor@acme.example",   new DateOnly(2021, 3, 15), deptEngId,     posSenDevId,     empCtoId,     new DateOnly(1988, 7, 22),  "Nigerian",  "Male",   "james.okafor@outlook.com",   "07700 900002", "27 Coldharbour Lane",  "Flat 4",   "London",      "Greater London",     "SE5 9NR",   "ACME-002", etPermId),
                MakeAcme(empSenDev2Id,  "Priya",  "Sharma",   "priya.sharma@acme.example",   new DateOnly(2021, 9, 1),  deptEngId,     posSenDevId,     empCtoId,     new DateOnly(1990, 11, 5),  "Indian",    "Female", "priya.sharma@gmail.com",     "07700 900003", "8 Brick Lane",         "Apt 2B",   "London",      "Greater London",     "E1 6RF",    "ACME-003", etPermId),
                MakeAcme(empDev1Id,     "Tom",    "Williams", "tom.williams@acme.example",   new DateOnly(2023, 2, 20), deptEngId,     posDevId,        empSenDev1Id, new DateOnly(1996, 4, 12),  "British",   "Male",   "tom.williams@hotmail.com",   "07700 900004", "52 Didsbury Road",     null,       "Manchester",  "Greater Manchester", "M20 5LH",   "ACME-004", etFixedTermId),
                MakeAcme(empHrMgrId,    "Laura",  "Bennett",  "laura.bennett@acme.example",  new DateOnly(2019, 6, 3),  deptHrId,      posHrMgrId,      null,         new DateOnly(1979, 9, 28),  "British",   "Female", "laura.bennett@gmail.com",    "07700 900005", "3 Thornton Avenue",    null,       "London",      "Greater London",     "SW2 4HX",   "ACME-005", etPermId),
                MakeAcme(empHrAdvId,    "Marcus", "Diallo",   "marcus.diallo@acme.example",  new DateOnly(2022, 11, 7), deptHrId,      posHrAdvisorId,  empHrMgrId,   new DateOnly(1991, 2, 14),  "French",    "Male",   "marcus.diallo@gmail.com",    "07700 900006", "19 Seven Sisters Road","Floor 2",  "London",      "Greater London",     "N4 2BY",    "ACME-006", etPermId),
                MakeAcme(empFinMgrId,   "Sophie", "Laurent",  "sophie.laurent@acme.example", new DateOnly(2020, 4, 14), deptFinanceId, posFinanceMgrId, null,         new DateOnly(1985, 6, 30),  "French",    "Female", "sophie.laurent@gmail.com",   "07700 900007", "61 Gloucester Road",   null,       "London",      "Greater London",     "SW7 4PE",   "ACME-007", etPermId),
                MakeAcme(empSalesMgrId, "David",  "Park",     "david.park@acme.example",     new DateOnly(2018, 8, 22), deptSalesId,   posSalesMgrId,   null,         new DateOnly(1975, 12, 8),  "Korean",    "Male",   "david.park@outlook.com",     "07700 900008", "44 Harborne Park Road",null,       "Birmingham",  "West Midlands",      "B17 0DH",   "ACME-008", etPermId),
                MakeAcme(empAe1Id,      "Emma",   "Jones",    "emma.jones@acme.example",     new DateOnly(2023, 5, 2),  deptSalesId,   posAeId,         empSalesMgrId, new DateOnly(1998, 8, 17), "British",   "Female", "emma.jones@gmail.com",       "07700 900009", "11 Cowley Road",       "Flat 1",   "Oxford",      "Oxfordshire",        "OX4 1HZ",   "ACME-009", etPermId),
                MakeAcme(empAe2Id,      "Carlos", "Rivera",   "carlos.rivera@acme.example",  new DateOnly(2024, 1, 8),  deptSalesId,   posAeId,         empSalesMgrId, new DateOnly(2000, 1, 25), "Spanish",   "Male",   "carlos.rivera@gmail.com",    "07700 900010", "5 Western Road",       null,       "Brighton",    "East Sussex",        "BN1 2DA",   "ACME-010", etContractId),
                MakeAcme(empCfoId,      "Priya",  "Shah",     "priya.shah@acme.example",     new DateOnly(2019, 3, 1),  deptFinanceId, posCfoId,        null,          new DateOnly(1980, 4, 9),  "British",   "Female", "priya.shah@gmail.com",       "07700 900011", "22 Chiswick High Road", null,     "London",      "Greater London",     "W4 2DT",    "ACME-011", etPermId));

            await db.SaveChangesAsync();

            var ctoStartingSalary = Compensation.Create(
                Guid.Parse("50000000-0000-0000-0000-000000000002"), acmeId, empCtoId,
                new DateOnly(2020, 1, 6), SalaryType.Annual, 120000m, "GBP", 37.5m, 1m,
                "Starting salary", CompensationChangeReason.NewHire, empHrMgrId, now);
            ctoStartingSalary.Close(new DateOnly(2022, 12, 31), now);

            var ctoCurrentSalary = Compensation.Create(
                Guid.Parse("50000000-0000-0000-0000-000000000001"), acmeId, empCtoId,
                new DateOnly(2023, 1, 1), SalaryType.Annual, 145000m, "GBP", 37.5m, 1m,
                "Promoted to CTO", CompensationChangeReason.Promotion, empHrMgrId, now);

            db.Compensations.AddRange(ctoStartingSalary, ctoCurrentSalary);

            // Every employee needs at least one starting Compensation record and one "Employee
            // joined" Timeline entry — CreateEmployeeHandler/EmployeeCreatedHandler produce both
            // automatically for employees created through the app, but these employees are
            // inserted directly via db.Employees.AddRange above and bypass that handler entirely,
            // so both need to be seeded explicitly here. Sarah Chen (CTO) already has her own
            // compensation history above and is included below only for her timeline entry.
            var newHireCompensation = new (Guid EmployeeId, DateOnly StartDate, decimal Salary)[]
            {
                (empSenDev1Id,  new DateOnly(2021, 3, 15),  85000m),
                (empSenDev2Id,  new DateOnly(2021, 9, 1),   82000m),
                (empDev1Id,     new DateOnly(2023, 2, 20),  55000m),
                (empHrMgrId,    new DateOnly(2019, 6, 3),   70000m),
                (empHrAdvId,    new DateOnly(2022, 11, 7),  45000m),
                (empFinMgrId,   new DateOnly(2020, 4, 14),  75000m),
                (empSalesMgrId, new DateOnly(2018, 8, 22),  72000m),
                (empAe1Id,      new DateOnly(2023, 5, 2),   40000m),
                (empAe2Id,      new DateOnly(2024, 1, 8),   38000m),
                (empCfoId,      new DateOnly(2019, 3, 1),   135000m),
            };

            foreach (var (employeeId, startDate, salary) in newHireCompensation)
            {
                db.Compensations.Add(Compensation.Create(
                    Guid.NewGuid(), acmeId, employeeId, startDate, SalaryType.Annual, salary, "GBP", 37.5m, 1m,
                    "Starting salary", CompensationChangeReason.NewHire, empHrMgrId, now));
            }

            var allAcmeEmployeeStartDates = new (Guid EmployeeId, DateOnly StartDate)[]
            {
                (empCtoId,      new DateOnly(2020, 1, 6)),
                (empSenDev1Id,  new DateOnly(2021, 3, 15)),
                (empSenDev2Id,  new DateOnly(2021, 9, 1)),
                (empDev1Id,     new DateOnly(2023, 2, 20)),
                (empHrMgrId,    new DateOnly(2019, 6, 3)),
                (empHrAdvId,    new DateOnly(2022, 11, 7)),
                (empFinMgrId,   new DateOnly(2020, 4, 14)),
                (empSalesMgrId, new DateOnly(2018, 8, 22)),
                (empAe1Id,      new DateOnly(2023, 5, 2)),
                (empAe2Id,      new DateOnly(2024, 1, 8)),
                (empCfoId,      new DateOnly(2019, 3, 1)),
            };

            foreach (var (employeeId, startDate) in allAcmeEmployeeStartDates)
            {
                db.EmployeeTimelineEntries.Add(EmployeeTimelineEntry.Create(
                    Guid.NewGuid(), acmeId, employeeId, startDate,
                    EmployeeTimelineEventType.EmployeeJoined, EmployeeTimelineCategory.Employment,
                    "Employee joined", "Employee joined the company.",
                    performedByUserId: null, "Employees", sourceRecordId: null,
                    EmployeeTimelineVisibility.AuthorisedInternal, now));
            }

            // Sarah Chen (empCtoId) gets a handful of additional realistic timeline entries beyond
            // the plain "Employee joined" every seeded employee already has — she's the longest-
            // tenured, most-promoted employee in the seed data (joined 2020, promoted to CTO 2023
            // with her own compensation history above), so her timeline is the natural one for
            // demoing/screenshotting a fuller history rather than a single-entry timeline.
            db.EmployeeTimelineEntries.AddRange(
                EmployeeTimelineEntry.Create(
                    Guid.NewGuid(), acmeId, empCtoId, new DateOnly(2020, 4, 6),
                    EmployeeTimelineEventType.ProbationPassed, EmployeeTimelineCategory.OnboardingAndProbation,
                    "Probation passed", "Successfully completed probationary period.",
                    performedByUserId: null, "Probation", sourceRecordId: null,
                    EmployeeTimelineVisibility.AuthorisedInternal, now),
                EmployeeTimelineEntry.Create(
                    Guid.NewGuid(), acmeId, empCtoId, new DateOnly(2020, 4, 20),
                    EmployeeTimelineEventType.OnboardingCompleted, EmployeeTimelineCategory.OnboardingAndProbation,
                    "Onboarding completed", "Completed all onboarding tasks.",
                    performedByUserId: null, "Onboarding", sourceRecordId: null,
                    EmployeeTimelineVisibility.AuthorisedInternal, now),
                EmployeeTimelineEntry.Create(
                    Guid.NewGuid(), acmeId, empCtoId, new DateOnly(2021, 11, 15),
                    EmployeeTimelineEventType.LocationChanged, EmployeeTimelineCategory.Employment,
                    "Location changed", "Relocated to the London Head Office.",
                    performedByUserId: null, "Employees", sourceRecordId: null,
                    EmployeeTimelineVisibility.AuthorisedInternal, now),
                EmployeeTimelineEntry.Create(
                    Guid.NewGuid(), acmeId, empCtoId, new DateOnly(2023, 1, 1),
                    EmployeeTimelineEventType.EmployeePromoted, EmployeeTimelineCategory.Employment,
                    "Promoted to CTO", "Promoted from Engineering Manager to Chief Technology Officer.",
                    performedByUserId: null, "Employees", sourceRecordId: null,
                    EmployeeTimelineVisibility.AuthorisedInternal, now),
                EmployeeTimelineEntry.Create(
                    Guid.NewGuid(), acmeId, empCtoId, new DateOnly(2023, 1, 1),
                    EmployeeTimelineEventType.CompensationChanged, EmployeeTimelineCategory.Compensation,
                    "Compensation updated", "Salary increased to £145,000 following promotion to CTO.",
                    performedByUserId: null, "Employees", sourceRecordId: ctoCurrentSalary.Id,
                    EmployeeTimelineVisibility.AuthorisedInternal, now),
                EmployeeTimelineEntry.Create(
                    Guid.NewGuid(), acmeId, empCtoId, new DateOnly(2024, 6, 10),
                    EmployeeTimelineEventType.EmployeeDetailsCorrected, EmployeeTimelineCategory.Employment,
                    "Contact details updated", "Personal email and phone number updated.",
                    performedByUserId: null, "Employees", sourceRecordId: null,
                    EmployeeTimelineVisibility.AuthorisedInternal, now));

            await db.SaveChangesAsync();

            // ── E2E test pool ────────────────────────────────────────────────
            // Deterministic Acme employees consumed as *arrange* by E2E tests that need "an
            // employee to act on" but aren't testing employee creation. Mirrored verbatim in
            // tests/HR.Web.E2E.Tests/Infrastructure/SeededE2eEmployees.cs. Only seeded when the
            // host runs in Development (E2E) — never in Test/Staging/Production. Each pool member
            // gets the same starting Compensation + "Employee joined" timeline entry every other
            // seeded employee gets (they bypass CreateEmployeeHandler/EmployeeCreatedHandler).
            if (includeE2eTestPool)
            {
                var e2eDob = new DateOnly(1990, 6, 15);
                var e2eStart = new DateOnly(2026, 3, 1);
                var davidParkId = empSalesMgrId;

                foreach (var (_, id, lastName, email, employeeNumber, managedByDavidPark) in E2eTestPool)
                {
                    db.Employees.Add(MakeAcme(
                        id, "E2E", lastName, email, e2eStart,
                        deptEngId, posQaEngId, managedByDavidPark ? davidParkId : (Guid?)null,
                        e2eDob, "British", "Male",
                        null, null,
                        "1 Test Street", null, "London", "Greater London", "EC1A 1AA",
                        employeeNumber, etPermId));

                    db.Compensations.Add(Compensation.Create(
                        Guid.NewGuid(), acmeId, id, e2eStart, SalaryType.Annual, 50000m, "GBP", 37.5m, 1m,
                        "Starting salary", CompensationChangeReason.NewHire, empHrMgrId, now));

                    db.EmployeeTimelineEntries.Add(EmployeeTimelineEntry.Create(
                        Guid.NewGuid(), acmeId, id, e2eStart,
                        EmployeeTimelineEventType.EmployeeJoined, EmployeeTimelineCategory.Employment,
                        "Employee joined", "Employee joined the company.",
                        performedByUserId: null, "Employees", sourceRecordId: null,
                        EmployeeTimelineVisibility.AuthorisedInternal, now));
                }

                await db.SaveChangesAsync();
            }
        }

        // ── Beta Corp ─────────────────────────────────────────────────────────
        var betaCorpId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        if (!await db.Employees.AnyAsync(e => e.CompanyId == betaCorpId))
        {
            var betaDeptEngId  = Guid.Parse("10000000-0000-0000-0000-000000000011");
            var betaPosEngMgrId = Guid.Parse("20000000-0000-0000-0000-000000000011");
            var betaPosDevId    = Guid.Parse("20000000-0000-0000-0000-000000000012");
            var betaEmpMgrId    = Guid.Parse("30000000-0000-0000-0000-000000000011");
            var betaEmpDevId    = Guid.Parse("30000000-0000-0000-0000-000000000012");
            var betaEmpHrId     = Guid.Parse("30000000-0000-0000-0000-000000000015");

            var betaEtPermId = Guid.Parse("40000000-0000-0000-0000-000000000011");
            db.EmploymentTypes.Add(EmploymentType.Create(betaEtPermId, betaCorpId, "Permanent", null, now));

            db.Departments.Add(
                Department.Create(betaDeptEngId, betaCorpId, "Engineering", "Software engineering", now));

            var betaLocTypeOfficeId = Guid.Parse("60000000-0000-0000-0000-000000000011");
            var betaLocLeedsId      = Guid.Parse("70000000-0000-0000-0000-000000000011");

            db.LocationTypes.Add(LocationType.Create(betaLocTypeOfficeId, betaCorpId, "Office", null, now));
            db.Locations.Add(Location.Create(betaLocLeedsId, betaCorpId, betaLocTypeOfficeId, "Leeds Office", null, now));

            // Shared with HR.Modules.Leave's seed data — see the acmeLeavePolicyId comment above.
            var betaLeavePolicyId = Guid.Parse("C0000000-0000-0000-0000-000000000002");

            db.PositionProfiles.AddRange(
                PositionProfile.Create(betaPosEngMgrId, betaCorpId, betaDeptEngId, betaLocLeedsId, "Engineering Manager", null, null, null, null, null, null, null, betaLeavePolicyId, now),
                PositionProfile.Create(betaPosDevId,    betaCorpId, betaDeptEngId, betaLocLeedsId, "Software Developer",  null, null, null, null, null, null, null, betaLeavePolicyId, now));

            Employee MakeBeta(Guid id, string first, string last, string email, DateOnly start,
                              Guid posId, Guid? managerId, DateOnly dob, string nationality, string gender,
                              string? personalEmail, string? phone,
                              string addr1, string? addr2, string city, string? county, string postCode,
                              string employeeNumber, Guid employmentTypeId)
            {
                var e = Employee.Create(
                    id, betaCorpId, first, last, email, start, hasSystemAccess: true,
                    dob, nationality, gender, employeeNumber, employmentTypeId,
                    betaDeptEngId, betaLocLeedsId, posId, now);
                e.Assign(betaDeptEngId, posId, betaLocLeedsId, managerId, now);
                e.UpdatePersonalDetails(first, dob, nationality, gender, null, now);
                e.UpdateContactDetails(personalEmail, phone, null, addr1, addr2, city, county, postCode, "United Kingdom", now);
                e.UpdateEmploymentDetails(employeeNumber, employmentTypeId, start, null, null, null, null, now);
                e.Activate(now);
                return e;
            }

            db.Employees.AddRange(
                MakeBeta(betaEmpMgrId, "Alice", "Morgan", "alice.morgan@betacorp.example", new DateOnly(2022, 3, 1), betaPosEngMgrId, null,         new DateOnly(1987, 5, 20),  "British", "Female", "alice.morgan@gmail.com", "07700 900021", "33 Headingley Lane", null,     "Leeds", "West Yorkshire", "LS6 1BL", "BETA-001", betaEtPermId),
                MakeBeta(betaEmpDevId, "Bob",   "Taylor", "bob.taylor@betacorp.example",   new DateOnly(2023, 9, 4), betaPosDevId,    betaEmpMgrId, new DateOnly(1993, 10, 11), "British", "Male",   "bob.taylor@hotmail.com", "07700 900022", "7 Kirkstall Road",   "Flat 2", "Leeds", "West Yorkshire", "LS3 1LH", "BETA-002", betaEtPermId),
                // HR Administrator for Beta Corp — gives HrSettingsPageTests (which mutates the
                // shared CompanySettings row's Employee Numbering mode mid-test) a tenant fully
                // isolated from every other role-fixed test's Acme-based employee creation, rather
                // than racing them on Acme's own shared settings row.
                MakeBeta(betaEmpHrId,  "Grace", "Kim",    "grace.kim@betacorp.example",     new DateOnly(2021, 6, 1), betaPosEngMgrId, null,         new DateOnly(1985, 2, 14),  "British", "Female", "grace.kim@gmail.com",   "07700 900023", "12 Kirkgate",        null,     "Leeds", "West Yorkshire", "LS1 6BY", "BETA-003", betaEtPermId));

            await db.SaveChangesAsync();

            // Same rationale as the Acme block above — every employee needs a starting
            // Compensation record and an "Employee joined" Timeline entry, and seeded employees
            // bypass CreateEmployeeHandler entirely.
            var betaNewHireCompensation = new (Guid EmployeeId, DateOnly StartDate, decimal Salary)[]
            {
                (betaEmpMgrId, new DateOnly(2022, 3, 1),  78000m),
                (betaEmpDevId, new DateOnly(2023, 9, 4),  52000m),
            };

            foreach (var (employeeId, startDate, salary) in betaNewHireCompensation)
            {
                db.Compensations.Add(Compensation.Create(
                    Guid.NewGuid(), betaCorpId, employeeId, startDate, SalaryType.Annual, salary, "GBP", 37.5m, 1m,
                    "Starting salary", CompensationChangeReason.NewHire, betaEmpMgrId, now));
            }

            foreach (var (employeeId, startDate) in betaNewHireCompensation.Select(c => (c.EmployeeId, c.StartDate)))
            {
                db.EmployeeTimelineEntries.Add(EmployeeTimelineEntry.Create(
                    Guid.NewGuid(), betaCorpId, employeeId, startDate,
                    EmployeeTimelineEventType.EmployeeJoined, EmployeeTimelineCategory.Employment,
                    "Employee joined", "Employee joined the company.",
                    performedByUserId: null, "Employees", sourceRecordId: null,
                    EmployeeTimelineVisibility.AuthorisedInternal, now));
            }

            await db.SaveChangesAsync();
        }
    }
}
