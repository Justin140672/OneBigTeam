using FluentValidation;
using Hangfire;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Jobs;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Features.CreateDocumentRequestsOnEmployeeCreated;
using HR.Modules.Documents.Features.UploadRequestedDocument;
using HR.Modules.Documents.Features.CreateDocumentType;
using HR.Modules.Documents.Features.DeactivateDocumentType;
using HR.Modules.Documents.Features.CreateCompanyDocumentCategory;
using HR.Modules.Documents.Features.UpdateCompanyDocumentCategory;
using HR.Modules.Documents.Features.DeactivateCompanyDocumentCategory;
using HR.Modules.Documents.Features.ListCompanyDocumentCategories;
using HR.Modules.Documents.Features.UploadSharedCompanyDocument;
using HR.Modules.Documents.Features.UploadSharedCompanyDocumentVersion;
using HR.Modules.Documents.Features.ListSharedCompanyDocuments;
using HR.Modules.Documents.Features.ListPublishedSharedCompanyDocuments;
using HR.Modules.Documents.Features.GetSharedCompanyDocument;
using HR.Modules.Documents.Features.GetSharedCompanyDocumentAcknowledgementProgress;
using HR.Modules.Documents.Features.GetPublishedSharedCompanyDocument;
using HR.Modules.Documents.Features.GetSharedCompanyDocumentAuditHistory;
using HR.Modules.Documents.Features.DownloadSharedCompanyDocument;
using HR.Modules.Documents.Features.DownloadSharedCompanyDocumentVersion;
using HR.Modules.Documents.Features.AcknowledgeSharedCompanyDocument;
using HR.Modules.Documents.Features.UpdateSharedCompanyDocumentMetadata;
using HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAudience;
using HR.Modules.Documents.Features.PublishSharedCompanyDocument;
using HR.Modules.Documents.Features.ArchiveSharedCompanyDocument;
using HR.Modules.Documents.Features.ExpireSharedCompanyDocument;
using HR.Modules.Documents.Features.CompleteSharedCompanyDocumentReview;
using HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAcknowledgementSettings;
using HR.Modules.Documents.Features.GetEmployeeAcknowledgementHistory;
using HR.Modules.Documents.Features.ReissueSharedCompanyDocumentAcknowledgement;
using HR.Modules.Documents.Features.DeleteEmployeeDocument;
using HR.Modules.Documents.Features.DownloadEmployeeDocument;
using HR.Modules.Documents.Features.GetEmployeeDocument;
using HR.Modules.Documents.Features.GetArchivedEmployeeDocuments;
using HR.Modules.Documents.Features.RestoreEmployeeDocument;
using HR.Modules.Documents.Features.PurgeEligibleArchivedEmployeeDocuments;
using HR.Modules.Documents.Features.GetEmployeeProfilePhoto;
using HR.Modules.Documents.Features.GetExpiringDocuments;
using HR.Modules.Documents.Features.ListSharedCompanyDocumentsDueForReview;
using HR.Modules.Documents.Features.ProcessDocumentExpiryNotifications;
using HR.Modules.Documents.Features.GetDocumentRequest;
using HR.Modules.Documents.Features.ListDocumentRequests;
using HR.Modules.Documents.Features.CancelDocumentRequest;
using HR.Modules.Documents.Features.RequestAdditionalEmployeeDocument;
using HR.Modules.Documents.Features.ListEmployeeDocuments;
using HR.Modules.Documents.Features.SearchEmployeeDocuments;
using HR.Modules.Documents.Features.UploadEmployeeDocument;
using HR.Modules.Documents.Features.UploadEmployeeDocumentVersion;
using HR.Modules.Documents.Features.GetEmployeeDocumentVersionHistory;
using HR.Modules.Documents.Features.UploadEmployeeProfilePhoto;
using HR.Modules.Documents.Features.UploadMyProfilePhoto;
using HR.Modules.Documents.Features.CancelPendingProfilePhoto;
using HR.Modules.Documents.Features.GetMyProfilePhoto;
using HR.Modules.Documents.Features.GetPendingProfilePhoto;
using HR.Modules.Documents.Features.GetPendingProfilePhotoById;
using HR.Modules.Documents.Features.ApproveProfilePhoto;
using HR.Modules.Documents.Features.RejectProfilePhoto;
using HR.Modules.Documents.Features.CompleteProfilePhotoReviewFromTask;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Services.OnboardingTasks;
using HR.Modules.Documents.Features.ListDocumentTypes;
using HR.Modules.Documents.Features.UpdateDocumentType;
using HR.Modules.Documents.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace HR.Modules.Documents;

public static class DocumentsModule
{
    public static IServiceCollection AddDocumentsModule(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        AddFeatureServices(services);
        AddStorageService(services, configuration, environment);
        AddProfilePhotoServices(services, configuration);

        services.AddDbContext<DocumentsDbContext>(options =>
            options.UseVersionedAggregates().UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "documents")));

        return services;
    }

    private static void AddStorageService(
        IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.TryAddSingleton(environment);

        services.Configure<FileUploadOptions>(configuration.GetSection("Documents:FileUpload"));
        services.AddScoped<IFileUploadValidator, FileUploadValidator>();

        var isE2ETestingForVirusScan = string.Equals(
            Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase);

        // Security review ticket 2 (P1): the no-op scanner marks every upload "clean" without ever
        // inspecting it. That is only acceptable in Development or an explicit automated-test
        // environment (IsDevelopment(), the "Test" environment name used by WebApplicationFactory-
        // hosted integration tests, or the E2E harness above) — never in Staging/Production, even if
        // ClamAv configuration happens to be absent there.
        var isNoOpAllowedEnvironment =
            isE2ETestingForVirusScan
            || environment.IsDevelopment()
            || environment.IsEnvironment("Test");

        var clamAvSection = configuration.GetSection("Documents:ClamAv");
        var clamAvHost = clamAvSection["Host"];
        var hasClamAvConfig = clamAvSection.Exists() && !string.IsNullOrWhiteSpace(clamAvHost);

        // Security review ticket 4 (P1): expose whichever scanner is selected below to other
        // modules via the shared HR.Infrastructure.Abstractions.IUploadedFileScanner contract.
        services.AddScoped<HR.Infrastructure.Abstractions.IUploadedFileScanner, UploadedFileScannerAdapter>();

        if (!isE2ETestingForVirusScan && hasClamAvConfig)
        {
            services.AddOptions<ClamAvOptions>().Bind(clamAvSection).ValidateOnStart();
            services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<ClamAvOptions>, ClamAvOptionsValidator>();
            services.AddScoped<IVirusScanService, ClamAvVirusScanService>();
            services.AddHealthChecks().AddCheck<ClamAvHealthCheck>(
                "clam-av",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "critical"]);
        }
        else if (isNoOpAllowedEnvironment)
        {
            services.AddScoped<IVirusScanService, NoOpVirusScanService>();
        }
        else
        {
            throw new InvalidOperationException(
                "Malware scanning is not configured for this environment. "
                + "'Documents:ClamAv:Host' (and Port/TimeoutSeconds) must be set in Staging/Production — "
                + "the no-op scanner that marks every upload as clean is only permitted in Development "
                + "or an explicit automated-test environment.");
        }

        var supabaseSection = configuration.GetSection("Documents:Supabase");

        if (supabaseSection.Exists() && !string.IsNullOrWhiteSpace(supabaseSection["SupabaseUrl"]))
        {
            services.AddOptions<SupabaseStorageOptions>().Bind(supabaseSection).ValidateOnStart();
            services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<SupabaseStorageOptions>, SupabaseStorageOptionsValidator>();
            services.AddHttpClient<IDocumentStorageService, SupabaseDocumentStorageService>();
            services.AddHealthChecks().AddCheck<DocumentStorageHealthCheck>(
                "document-storage", tags: ["degraded"]);
        }
        else if (isNoOpAllowedEnvironment)
        {
            // Security review ticket 3 (P1): the local (temp-dir) storage fallback loses every file on
            // restart/redeploy and serves downloads through a dev-only route — only acceptable in the
            // same Development/explicit-test environments as the no-op virus scanner above.
            services.AddScoped<IDocumentStorageService, LocalDocumentStorageService>();
            Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<ILocalStorageUrlSigner>(
                services, _ => new LocalStorageUrlSigner(TimeProvider.System));
        }
        else
        {
            throw new InvalidOperationException(
                "Document storage is not configured for this environment. "
                + "'Documents:Supabase:SupabaseUrl' (and ServiceRoleKey/BucketName) must be set in "
                + "Staging/Production — the local temp-directory fallback is only permitted in "
                + "Development or an explicit automated-test environment.");
        }

        if (isNoOpAllowedEnvironment)
        {
            services.AddScoped<ILocalStorageObjectResolver, DocumentsLocalStorageObjectResolver>();
            services.AddScoped<ILocalStorageObjectResolver, ProfilePhotoLocalStorageObjectResolver>();
        }
    }

    private static void AddProfilePhotoServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ImageUploadOptions>(configuration.GetSection("Documents:ProfilePhoto:Upload"));
        services.AddScoped<IImageUploadValidator, ImageUploadValidator>();
        services.AddScoped<IDocumentTypeDefaultsProvisioner, DocumentTypeDefaultsProvisioner>();
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<CreateDocumentTypeHandler>();
        services.AddScoped<IValidator<CreateDocumentTypeRequest>, CreateDocumentTypeValidator>();

        services.AddScoped<UpdateDocumentTypeHandler>();
        services.AddScoped<IValidator<UpdateDocumentTypeRequest>, UpdateDocumentTypeValidator>();

        services.AddScoped<ListDocumentTypesHandler>();
        services.AddScoped<IValidator<ListDocumentTypesRequest>, ListDocumentTypesValidator>();

        services.AddScoped<DeactivateDocumentTypeHandler>();

        services.AddScoped<CreateCompanyDocumentCategoryHandler>();
        services.AddScoped<IValidator<CreateCompanyDocumentCategoryRequest>, CreateCompanyDocumentCategoryValidator>();

        services.AddScoped<UpdateCompanyDocumentCategoryHandler>();
        services.AddScoped<IValidator<UpdateCompanyDocumentCategoryRequest>, UpdateCompanyDocumentCategoryValidator>();

        services.AddScoped<ListCompanyDocumentCategoriesHandler>();
        services.AddScoped<IValidator<ListCompanyDocumentCategoriesRequest>, ListCompanyDocumentCategoriesValidator>();

        services.AddScoped<DeactivateCompanyDocumentCategoryHandler>();

        services.AddScoped<UploadSharedCompanyDocumentHandler>();
        services.AddScoped<IValidator<UploadSharedCompanyDocumentRequest>, UploadSharedCompanyDocumentValidator>();

        services.AddScoped<UploadSharedCompanyDocumentVersionHandler>();
        services.AddScoped<IValidator<UploadSharedCompanyDocumentVersionRequest>, UploadSharedCompanyDocumentVersionValidator>();

        services.AddScoped<ListSharedCompanyDocumentsHandler>();
        services.AddScoped<ListPublishedSharedCompanyDocumentsHandler>();
        services.AddScoped<GetSharedCompanyDocumentHandler>();
        services.AddScoped<GetSharedCompanyDocumentAcknowledgementProgressHandler>();
        services.AddScoped<GetPublishedSharedCompanyDocumentHandler>();
        services.AddScoped<GetSharedCompanyDocumentAuditHistoryHandler>();
        services.AddScoped<DownloadSharedCompanyDocumentHandler>();
        services.AddScoped<DownloadSharedCompanyDocumentVersionHandler>();
        services.AddScoped<AcknowledgeSharedCompanyDocumentHandler>();
        services.AddScoped<IValidator<AcknowledgeSharedCompanyDocumentRequest>, AcknowledgeSharedCompanyDocumentValidator>();

        services.AddScoped<UpdateSharedCompanyDocumentMetadataHandler>();
        services.AddScoped<IValidator<UpdateSharedCompanyDocumentMetadataRequest>, UpdateSharedCompanyDocumentMetadataValidator>();

        services.AddScoped<UpdateSharedCompanyDocumentAudienceHandler>();
        services.AddScoped<IValidator<UpdateSharedCompanyDocumentAudienceRequest>, UpdateSharedCompanyDocumentAudienceValidator>();

        services.AddScoped<PublishSharedCompanyDocumentHandler>();

        services.AddScoped<ArchiveSharedCompanyDocumentHandler>();
        services.AddScoped<IValidator<ArchiveSharedCompanyDocumentRequest>, ArchiveSharedCompanyDocumentValidator>();

        services.AddScoped<ExpireSharedCompanyDocumentHandler>();
        services.AddScoped<IValidator<ExpireSharedCompanyDocumentRequest>, ExpireSharedCompanyDocumentValidator>();

        services.AddScoped<CompleteSharedCompanyDocumentReviewHandler>();
        services.AddScoped<IValidator<CompleteSharedCompanyDocumentReviewRequest>, CompleteSharedCompanyDocumentReviewValidator>();

        services.AddScoped<UpdateSharedCompanyDocumentAcknowledgementSettingsHandler>();
        services.AddScoped<IValidator<UpdateSharedCompanyDocumentAcknowledgementSettingsRequest>, UpdateSharedCompanyDocumentAcknowledgementSettingsValidator>();

        services.AddScoped<GetEmployeeAcknowledgementHistoryHandler>();
        services.AddScoped<IValidator<GetEmployeeAcknowledgementHistoryRequest>, GetEmployeeAcknowledgementHistoryValidator>();

        services.AddScoped<ReissueSharedCompanyDocumentAcknowledgementHandler>();
        services.AddScoped<IValidator<ReissueSharedCompanyDocumentAcknowledgementRequest>, ReissueSharedCompanyDocumentAcknowledgementValidator>();

        services.AddScoped<SharedCompanyDocumentAudienceRuleBuilder>();
        services.AddScoped<SharedCompanyDocumentAudienceMatcher>();
        services.AddScoped<SharedCompanyDocumentAudienceDescriber>();

        services.AddScoped<UploadEmployeeDocumentHandler>();
        services.AddScoped<IValidator<UploadEmployeeDocumentRequest>, UploadEmployeeDocumentValidator>();

        services.AddScoped<UploadEmployeeDocumentVersionHandler>();
        services.AddScoped<IValidator<UploadEmployeeDocumentVersionRequest>, UploadEmployeeDocumentVersionValidator>();

        services.AddScoped<GetEmployeeDocumentVersionHistoryHandler>();
        services.AddScoped<IValidator<GetEmployeeDocumentVersionHistoryRequest>, GetEmployeeDocumentVersionHistoryValidator>();

        services.AddScoped<UploadEmployeeProfilePhotoHandler>();
        services.AddScoped<IValidator<UploadEmployeeProfilePhotoRequest>, UploadEmployeeProfilePhotoValidator>();

        services.AddScoped<UploadMyProfilePhotoHandler>();
        services.AddScoped<IValidator<UploadMyProfilePhotoRequest>, UploadMyProfilePhotoValidator>();

        services.AddScoped<CancelPendingProfilePhotoHandler>();
        services.AddScoped<IValidator<CancelPendingProfilePhotoRequest>, CancelPendingProfilePhotoValidator>();

        services.AddScoped<GetMyProfilePhotoHandler>();

        services.AddScoped<GetPendingProfilePhotoHandler>();
        services.AddScoped<IValidator<GetPendingProfilePhotoRequest>, GetPendingProfilePhotoValidator>();

        services.AddScoped<GetPendingProfilePhotoByIdHandler>();
        services.AddScoped<IValidator<GetPendingProfilePhotoByIdRequest>, GetPendingProfilePhotoByIdValidator>();

        services.AddScoped<GetEmployeeProfilePhotoHandler>();
        services.AddScoped<IValidator<GetEmployeeProfilePhotoRequest>, GetEmployeeProfilePhotoValidator>();

        services.AddScoped<Services.ProfilePhotoReviewer>();

        services.AddScoped<ApproveProfilePhotoHandler>();
        services.AddScoped<IValidator<ApproveProfilePhotoRequest>, ApproveProfilePhotoValidator>();

        services.AddScoped<RejectProfilePhotoHandler>();
        services.AddScoped<IValidator<RejectProfilePhotoRequest>, RejectProfilePhotoValidator>();

        // Wires the generic Tasks "Complete task" endpoint into the real approve/reject decision
        // for a profile-photo review task, and prevents that endpoint from completing the task
        // without one (see CompleteProfilePhotoReviewFromTaskAction for details).
        services.AddScoped<ITaskCompletionAction, CompleteProfilePhotoReviewFromTaskAction>();

        services.AddScoped<ReconcileMissingProfilePhotoReviewTasksJob>();

        services.AddScoped<GetEmployeeDocumentHandler>();
        services.AddScoped<IValidator<GetEmployeeDocumentRequest>, GetEmployeeDocumentValidator>();

        services.AddScoped<ListEmployeeDocumentsHandler>();
        services.AddScoped<IValidator<ListEmployeeDocumentsRequest>, ListEmployeeDocumentsValidator>();

        services.AddScoped<SearchEmployeeDocumentsHandler>();
        services.AddScoped<IValidator<SearchEmployeeDocumentsRequest>, SearchEmployeeDocumentsValidator>();

        services.AddScoped<ListDocumentRequestsHandler>();
        services.AddScoped<GetDocumentRequestHandler>();
        services.AddScoped<RequestAdditionalEmployeeDocumentHandler>();
        services.AddScoped<CancelDocumentRequestHandler>();

        services.AddScoped<DeleteEmployeeDocumentHandler>();
        services.AddScoped<DownloadEmployeeDocumentHandler>();

        services.AddScoped<GetArchivedEmployeeDocumentsHandler>();
        services.AddScoped<IValidator<GetArchivedEmployeeDocumentsRequest>, GetArchivedEmployeeDocumentsValidator>();

        services.AddScoped<RestoreEmployeeDocumentHandler>();
        services.AddScoped<IValidator<RestoreEmployeeDocumentRequest>, RestoreEmployeeDocumentValidator>();

        services.AddScoped<PurgeEligibleArchivedEmployeeDocumentsHandler>();
        services.AddScoped<IValidator<PurgeEligibleArchivedEmployeeDocumentsRequest>, PurgeEligibleArchivedEmployeeDocumentsValidator>();

        services.AddScoped<Services.DocumentResourceAuthorizer>();

        services.AddScoped<GetExpiringDocumentsHandler>();
        services.AddScoped<IValidator<GetExpiringDocumentsRequest>, GetExpiringDocumentsValidator>();

        services.AddScoped<ListSharedCompanyDocumentsDueForReviewHandler>();
        services.AddScoped<IValidator<ListSharedCompanyDocumentsDueForReviewRequest>, ListSharedCompanyDocumentsDueForReviewValidator>();

        services.AddScoped<ProcessDocumentExpiryNotificationsHandler>();

        services.AddScoped<IDocumentTypeReader, DocumentTypeReader>();
        services.AddScoped<HR.Infrastructure.Abstractions.IDocumentDataExportManifest, Services.DocumentDataExportManifest>();
        services.AddScoped<IOutstandingDocumentRequestReader, OutstandingDocumentRequestReader>();
        services.AddScoped<IProfilePhotoReader, ProfilePhotoReader>();
        services.AddScoped<IDocumentComplianceReportReader, DocumentComplianceReportReader>();
        services.AddScoped<ICompanyDocumentAcknowledgementReportReader, CompanyDocumentAcknowledgementReportReader>();
        services.AddScoped<IDocumentStorageReader, DocumentStorageReader>();

        services.AddScoped<IPlatformDocumentActivityReader, PlatformDocumentActivityReader>();

        services.AddScoped<IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>, EmployeeCreatedHandler>();

        services.AddScoped<UploadRequestedDocumentHandler>();
        services.AddScoped<IValidator<UploadRequestedDocumentRequest>, UploadRequestedDocumentValidator>();

        services.AddScoped<SharedCompanyDocumentAcknowledgementReminderJob>();
        services.AddScoped<DetectDocumentsDueForReviewJob>();
        services.AddScoped<DocumentExpiryReminderJob>();
        services.AddHttpClient();
        services.AddScoped<ScanUploadedFileJob>();
        services.AddScoped<IdempotencyMaintenanceJob>();

        services.AddScoped<ISharedCompanyDocumentAcknowledgementHistoryReplayer, SharedCompanyDocumentAcknowledgementHistoryReplayer>();

        services.AddScoped<IWorkloadActionProvider, MissingRequiredEmployeeDocumentsWorkloadActionProvider>();
        services.AddScoped<IWorkloadActionProvider, EmployeeDocumentsExpiringSoonWorkloadActionProvider>();
        services.AddScoped<IWorkloadActionProvider, CompanyDocumentAcknowledgementsOutstandingWorkloadActionProvider>();
        services.AddScoped<IWorkloadActionProvider, SharedCompanyDocumentReviewsDueWorkloadActionProvider>();

        services.AddScoped<IOnboardingTaskDefinition, ReviewCompanyDocumentsTask>();
    }

    public static WebApplication UseDocumentsRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        jobManager.AddOrUpdate<SharedCompanyDocumentAcknowledgementReminderJob>(
            "shared-company-document-acknowledgement-reminders",
            job => job.ExecuteAsync(),
            Cron.Daily(9));
        jobManager.AddOrUpdate<DetectDocumentsDueForReviewJob>(
            "detect-documents-due-for-review",
            job => job.ExecuteAsync(),
            Cron.Daily(10));
        jobManager.AddOrUpdate<DocumentExpiryReminderJob>(
            "document-expiry-reminders",
            job => job.ExecuteAsync(),
            Cron.Daily(11));
        // Ticket 3 (P1) follow-up item 4: clean up expired idempotency records.
        jobManager.AddOrUpdate<IdempotencyMaintenanceJob>(
            "documents-idempotency-maintenance",
            job => job.ExecuteAsync(),
            "*/5 * * * *");
        jobManager.AddOrUpdate<ReconcileMissingProfilePhotoReviewTasksJob>(
            "reconcile-missing-profile-photo-review-tasks",
            job => job.ExecuteAsync(),
            Cron.Hourly());
        return app;
    }

    public static async Task MigrateDocumentsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS documents");
        await db.Database.MigrateAsync();
    }

    public static async Task SeedDocumentsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db  = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        var now = DateTimeOffset.UtcNow;

        var acmeId    = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var acmeHrMgr = Guid.Parse("30000000-0000-0000-0000-000000000005");

        var acmeContract       = Guid.Parse("50000000-0000-0000-0000-000000000001");
        var acmePassport       = Guid.Parse("50000000-0000-0000-0000-000000000002");
        var acmeDrivingLicence = Guid.Parse("50000000-0000-0000-0000-000000000003");
        var acmeRightToWork    = Guid.Parse("50000000-0000-0000-0000-000000000004");
        var acmeCertificate    = Guid.Parse("50000000-0000-0000-0000-000000000005");
        var acmeOther          = Guid.Parse("50000000-0000-0000-0000-000000000006");

        if (!await db.DocumentTypes.AnyAsync(dt => dt.CompanyId == acmeId))
        {
            db.DocumentTypes.AddRange(
                DocumentType.Create(acmeContract,       acmeId, "Contract",        null, now),
                DocumentType.Create(acmePassport,       acmeId, "Passport",         null, now),
                DocumentType.Create(acmeDrivingLicence, acmeId, "Driving Licence",  null, now),
                DocumentType.Create(acmeRightToWork,    acmeId, "Right To Work",    null, now),
                DocumentType.Create(acmeCertificate,    acmeId, "Certificate",      null, now),
                DocumentType.Create(acmeOther,          acmeId, "Other",            null, now));

            await db.SaveChangesAsync();
        }

        if (!await db.CompanyDocumentCategories.AnyAsync(c => c.CompanyId == acmeId))
        {
            db.CompanyDocumentCategories.AddRange(
                CompanyDocumentCategory.Create(Guid.Parse("c0000000-0000-0000-0000-000000000001"), acmeId, "Policy",             now),
                CompanyDocumentCategory.Create(Guid.Parse("c0000000-0000-0000-0000-000000000002"), acmeId, "Handbook",           now),
                CompanyDocumentCategory.Create(Guid.Parse("c0000000-0000-0000-0000-000000000003"), acmeId, "Procedure",          now),
                CompanyDocumentCategory.Create(Guid.Parse("c0000000-0000-0000-0000-000000000004"), acmeId, "Form",               now),
                CompanyDocumentCategory.Create(Guid.Parse("c0000000-0000-0000-0000-000000000005"), acmeId, "Guidance",           now),
                CompanyDocumentCategory.Create(Guid.Parse("c0000000-0000-0000-0000-000000000006"), acmeId, "Health and Safety",  now),
                CompanyDocumentCategory.Create(Guid.Parse("c0000000-0000-0000-0000-000000000007"), acmeId, "Other",              now));

            await db.SaveChangesAsync();
        }

        if (!await db.Documents.AnyAsync(d => d.CompanyId == acmeId))
        {
            var acmeDocuments = new[]
            {
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000001"), acmeId, null, "Employment Contract – Sarah Chen",   null, acmeContract,  "employment-contract-sarah-chen.pdf",   184320,  "application/pdf", "seed/acme/contracts/employment-contract-sarah-chen.pdf",   null,                      acmeHrMgr, now),
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000002"), acmeId, null, "Employment Contract – James Okafor", null, acmeContract,  "employment-contract-james-okafor.pdf", 184320,  "application/pdf", "seed/acme/contracts/employment-contract-james-okafor.pdf", null,                      acmeHrMgr, now),
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000003"), acmeId, null, "Employment Contract – Priya Sharma", null, acmeContract,  "employment-contract-priya-sharma.pdf", 184320,  "application/pdf", "seed/acme/contracts/employment-contract-priya-sharma.pdf", null,                      acmeHrMgr, now),
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000004"), acmeId, null, "Employment Contract – Tom Williams", null, acmeContract,  "employment-contract-tom-williams.pdf", 184320,  "application/pdf", "seed/acme/contracts/employment-contract-tom-williams.pdf", null,                      acmeHrMgr, now),
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000005"), acmeId, null, "Offer Letter – Tom Williams",        null, acmeOther,     "offer-letter-tom-williams.pdf",        102400,  "application/pdf", "seed/acme/contracts/offer-letter-tom-williams.pdf",        null,                      acmeHrMgr, now),
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000006"), acmeId, null, "Employee Handbook 2026",             null, acmeOther,     "employee-handbook-2026.pdf",          2097152,  "application/pdf", "seed/acme/other/employee-handbook-2026.pdf",               new DateOnly(2027, 1, 1),  acmeHrMgr, now),
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000007"), acmeId, null, "Remote Working Policy",             null, acmeOther,     "remote-working-policy.pdf",            307200,  "application/pdf", "seed/acme/other/remote-working-policy.pdf",                new DateOnly(2027, 6, 30), acmeHrMgr, now),
            };
            foreach (var seedDoc in acmeDocuments)
                seedDoc.MarkScanClean(now);
            db.Documents.AddRange(acmeDocuments);

            await db.SaveChangesAsync();
        }

        var expectedAcmeDocuments = new Dictionary<Guid, (string Title, Guid DocumentTypeId)>
        {
            [Guid.Parse("60000000-0000-0000-0000-000000000001")] = ("Employment Contract – Sarah Chen", acmeContract),
            [Guid.Parse("60000000-0000-0000-0000-000000000002")] = ("Employment Contract – James Okafor", acmeContract),
            [Guid.Parse("60000000-0000-0000-0000-000000000003")] = ("Employment Contract – Priya Sharma", acmeContract),
            [Guid.Parse("60000000-0000-0000-0000-000000000004")] = ("Employment Contract – Tom Williams", acmeContract),
            [Guid.Parse("60000000-0000-0000-0000-000000000005")] = ("Offer Letter – Tom Williams", acmeOther),
            [Guid.Parse("60000000-0000-0000-0000-000000000006")] = ("Employee Handbook 2026", acmeOther),
            [Guid.Parse("60000000-0000-0000-0000-000000000007")] = ("Remote Working Policy", acmeOther),
        };

        var acmeDocsToReconcile = await db.Documents
            .Where(d => expectedAcmeDocuments.Keys.Contains(d.Id))
            .ToListAsync();

        var reconciledAny = false;
        foreach (var doc in acmeDocsToReconcile)
        {
            var expected = expectedAcmeDocuments[doc.Id];
            if (doc.Title == expected.Title && doc.DocumentTypeId == expected.DocumentTypeId)
                continue;

            // Title/DocumentTypeId have private setters (no legitimate domain reason for a document's
            // recorded title or type to be edited after upload) — EF's change-tracker metadata can
            // still target them directly for this one-time data-correction path.
            db.Entry(doc).Property(nameof(Document.Title)).CurrentValue = expected.Title;
            db.Entry(doc).Property(nameof(Document.DocumentTypeId)).CurrentValue = expected.DocumentTypeId;
            reconciledAny = true;
        }

        if (reconciledAny)
            await db.SaveChangesAsync();

        if (!await db.DocumentRequests.AnyAsync(r => r.CompanyId == acmeId))
        {
            var empJamesId  = Guid.Parse("30000000-0000-0000-0000-000000000002");
            var empTomId    = Guid.Parse("30000000-0000-0000-0000-000000000004");
            var empCarlosId = Guid.Parse("30000000-0000-0000-0000-000000000010");

            db.DocumentRequests.AddRange(
                DocumentRequest.Create(
                    Guid.Parse("b0000000-0000-0000-0000-000000000001"),
                    acmeId, empTomId, acmePassport,
                    positionProfileRequiredDocumentId: null,
                    dueDate: null, isMandatory: true, notes: null, requestedByEmployeeId: null, now),

                DocumentRequest.Create(
                    Guid.Parse("b0000000-0000-0000-0000-000000000002"),
                    acmeId, empCarlosId, acmePassport,
                    positionProfileRequiredDocumentId: null,
                    dueDate: null, isMandatory: true, notes: null, requestedByEmployeeId: null, now),

                DocumentRequest.Create(
                    Guid.Parse("b0000000-0000-0000-0000-000000000003"),
                    acmeId, empCarlosId, acmeRightToWork,
                    positionProfileRequiredDocumentId: null,
                    dueDate: null, isMandatory: true, notes: null, requestedByEmployeeId: null, now),

                DocumentRequest.Create(
                    Guid.Parse("b0000000-0000-0000-0000-000000000004"),
                    acmeId, empJamesId, acmePassport,
                    positionProfileRequiredDocumentId: null,
                    dueDate: null, isMandatory: true, notes: null, requestedByEmployeeId: null, now),

                DocumentRequest.Create(
                    Guid.Parse("b0000000-0000-0000-0000-000000000005"),
                    acmeId, empJamesId, acmeRightToWork,
                    positionProfileRequiredDocumentId: null,
                    dueDate: null, isMandatory: true, notes: null, requestedByEmployeeId: null, now));

            await db.SaveChangesAsync();
        }

        if (!await db.EmployeeDocuments.AnyAsync(ed => ed.CompanyId == acmeId))
        {
            var empSarahId = Guid.Parse("30000000-0000-0000-0000-000000000001");
            var empJamesId = Guid.Parse("30000000-0000-0000-0000-000000000002");
            var empPriyaId = Guid.Parse("30000000-0000-0000-0000-000000000003");
            var empTomId   = Guid.Parse("30000000-0000-0000-0000-000000000004");

            var sarahContract  = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000001"), acmeId, empSarahId, Guid.Parse("60000000-0000-0000-0000-000000000001"), acmeHrMgr, now);
            var sarahHandbook  = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000002"), acmeId, empSarahId, Guid.Parse("60000000-0000-0000-0000-000000000006"), acmeHrMgr, now);
            var jamesContract  = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000003"), acmeId, empJamesId, Guid.Parse("60000000-0000-0000-0000-000000000002"), acmeHrMgr, now);
            var jamesPolicy    = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000004"), acmeId, empJamesId, Guid.Parse("60000000-0000-0000-0000-000000000007"), acmeHrMgr, now);
            var priyaContract  = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000005"), acmeId, empPriyaId, Guid.Parse("60000000-0000-0000-0000-000000000003"), acmeHrMgr, now);
            var priyaHandbook  = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000006"), acmeId, empPriyaId, Guid.Parse("60000000-0000-0000-0000-000000000006"), acmeHrMgr, now);
            var tomContract    = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000007"), acmeId, empTomId,   Guid.Parse("60000000-0000-0000-0000-000000000004"), acmeHrMgr, now);
            var tomOfferLetter = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000008"), acmeId, empTomId,   Guid.Parse("60000000-0000-0000-0000-000000000005"), acmeHrMgr, now);

            sarahContract.Acknowledge(now);
            sarahHandbook.Acknowledge(now);
            jamesContract.Acknowledge(now);
            priyaContract.Acknowledge(now);
            priyaHandbook.Acknowledge(now);
            tomOfferLetter.Acknowledge(now);

            db.EmployeeDocuments.AddRange(
                sarahContract, sarahHandbook,
                jamesContract, jamesPolicy,
                priyaContract, priyaHandbook,
                tomContract, tomOfferLetter);

            await db.SaveChangesAsync();
        }

        var betaCorpId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var betaEngMgr = Guid.Parse("30000000-0000-0000-0000-000000000011");

        var betaContract       = Guid.Parse("50000000-0000-0000-0000-000000000011");
        var betaPassport       = Guid.Parse("50000000-0000-0000-0000-000000000012");
        var betaDrivingLicence = Guid.Parse("50000000-0000-0000-0000-000000000013");
        var betaRightToWork    = Guid.Parse("50000000-0000-0000-0000-000000000014");
        var betaCertificate    = Guid.Parse("50000000-0000-0000-0000-000000000015");
        var betaOther          = Guid.Parse("50000000-0000-0000-0000-000000000016");

        if (!await db.DocumentTypes.AnyAsync(dt => dt.CompanyId == betaCorpId))
        {
            db.DocumentTypes.AddRange(
                DocumentType.Create(betaContract,       betaCorpId, "Contract",       null, now),
                DocumentType.Create(betaPassport,       betaCorpId, "Passport",        null, now),
                DocumentType.Create(betaDrivingLicence, betaCorpId, "Driving Licence", null, now),
                DocumentType.Create(betaRightToWork,    betaCorpId, "Right To Work",   null, now),
                DocumentType.Create(betaCertificate,    betaCorpId, "Certificate",     null, now),
                DocumentType.Create(betaOther,          betaCorpId, "Other",           null, now));

            await db.SaveChangesAsync();
        }

        if (!await db.CompanyDocumentCategories.AnyAsync(c => c.CompanyId == betaCorpId))
        {
            db.CompanyDocumentCategories.AddRange(
                CompanyDocumentCategory.Create(Guid.Parse("c0000000-0000-0000-0000-000000000011"), betaCorpId, "Policy",             now),
                CompanyDocumentCategory.Create(Guid.Parse("c0000000-0000-0000-0000-000000000012"), betaCorpId, "Handbook",           now),
                CompanyDocumentCategory.Create(Guid.Parse("c0000000-0000-0000-0000-000000000013"), betaCorpId, "Procedure",          now),
                CompanyDocumentCategory.Create(Guid.Parse("c0000000-0000-0000-0000-000000000014"), betaCorpId, "Form",               now),
                CompanyDocumentCategory.Create(Guid.Parse("c0000000-0000-0000-0000-000000000015"), betaCorpId, "Guidance",           now),
                CompanyDocumentCategory.Create(Guid.Parse("c0000000-0000-0000-0000-000000000016"), betaCorpId, "Health and Safety",  now),
                CompanyDocumentCategory.Create(Guid.Parse("c0000000-0000-0000-0000-000000000017"), betaCorpId, "Other",              now));

            await db.SaveChangesAsync();
        }

        if (!await db.Documents.AnyAsync(d => d.CompanyId == betaCorpId))
        {
            var betaDocuments = new[]
            {
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000011"), betaCorpId, null, "Employment Contract – Alice Morgan", null, betaContract, "employment-contract-alice-morgan.pdf",  184320,  "application/pdf", "seed/beta/contracts/employment-contract-alice-morgan.pdf", null,                     betaEngMgr, now),
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000012"), betaCorpId, null, "Employment Contract – Bob Taylor",   null, betaContract, "employment-contract-bob-taylor.pdf",    184320,  "application/pdf", "seed/beta/contracts/employment-contract-bob-taylor.pdf",   null,                     betaEngMgr, now),
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000013"), betaCorpId, null, "Employee Handbook 2026",             null, betaOther,    "employee-handbook-2026.pdf",           2097152,  "application/pdf", "seed/beta/other/employee-handbook-2026.pdf",               new DateOnly(2027, 1, 1), betaEngMgr, now),
            };
            foreach (var seedDoc in betaDocuments)
                seedDoc.MarkScanClean(now);
            db.Documents.AddRange(betaDocuments);

            await db.SaveChangesAsync();
        }

        if (!await db.EmployeeDocuments.AnyAsync(ed => ed.CompanyId == betaCorpId))
        {
            var empAliceId = Guid.Parse("30000000-0000-0000-0000-000000000011");
            var empBobId   = Guid.Parse("30000000-0000-0000-0000-000000000012");

            var aliceContract = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000011"), betaCorpId, empAliceId, Guid.Parse("60000000-0000-0000-0000-000000000011"), betaEngMgr, now);
            var bobContract   = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000012"), betaCorpId, empBobId,   Guid.Parse("60000000-0000-0000-0000-000000000012"), betaEngMgr, now);
            var bobHandbook   = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000013"), betaCorpId, empBobId,   Guid.Parse("60000000-0000-0000-0000-000000000013"), betaEngMgr, now);

            aliceContract.Acknowledge(now);
            bobContract.Acknowledge(now);

            db.EmployeeDocuments.AddRange(aliceContract, bobContract, bobHandbook);

            await db.SaveChangesAsync();
        }
    }
}
