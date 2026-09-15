using System.Reflection;

namespace HR.Architecture.Tests;

/// <summary>
/// Ticket 20: enforces adoption of the single canonical error-to-HTTP translator,
/// <see cref="HR.SharedKernel.ProblemResults.FromError(HR.SharedKernel.Error)"/>, across FastEndpoints
/// <c>Endpoint.cs</c> files.
///
/// This guards against a repeat of the Ticket 18 incident, where one endpoint hand-built its 409
/// response and silently dropped the machine-readable error code the web client relies on. Rather
/// than reflecting over compiled types (the mapping being detected is manual C# branching logic
/// inside a method body, which isn't observable via reflection), this test reads the raw source text
/// of every <c>src/Modules/**/Features/**/Endpoint.cs</c> file on disk.
///
/// Detection heuristic: a file is considered to have "manual" domain error-code mapping if it
/// contains the literal text <c>result.Error.Code ==</c> (i.e. it branches on the error code string
/// itself) and does NOT also contain <c>ProblemResults.FromError(</c>. A file that fully delegates to
/// <see cref="HR.SharedKernel.ProblemResults"/> never needs to reference <c>result.Error.Code</c>
/// directly, so this heuristic has essentially no false positives in practice (verified by manually
/// triaging every file it currently flags — see <see cref="BaselineUnmigratedManualMappingFiles"/>
/// below). A file that contains BOTH patterns would indicate leftover dead manual-mapping code
/// alongside a new <c>ProblemResults.FromError</c> call; as of writing there are none, but if one
/// appears in the future this test intentionally does NOT allow-list it — it must fail so the dead
/// code gets cleaned up as part of whatever change introduced it.
///
/// Ticket 20's endpoint-by-endpoint migration from manual mapping to <c>ProblemResults.FromError</c>
/// is a large, separately tracked, in-progress effort (roughly 100 of ~330 result-returning endpoints
/// migrated as of writing). This test does not require the backlog to be cleared. Instead it pins two
/// baselines so the backlog can only shrink, never grow:
/// <list type="bullet">
/// <item><description><see cref="DocumentedExceptionFiles"/> — endpoints that are permitted to keep
/// manual mapping *forever*, because they fall into one of the documented exception categories from
/// <see cref="HR.SharedKernel.ProblemResults"/>'s XML doc comment (information-hiding 404s, auth
/// challenge endpoints, file downloads, FastEndpoints-native validation responses, public endpoints
/// with deliberately limited error detail, or endpoints bound to a pre-existing different status-code
/// contract).</description></item>
/// <item><description><see cref="BaselineUnmigratedManualMappingFiles"/> — the remaining pre-existing
/// manual-mapping endpoints that have simply not been migrated to <c>ProblemResults.FromError</c> yet.
/// This list is a snapshot, not a policy: entries should be removed as endpoints get migrated (a
/// migrated endpoint disappears from the detection heuristic automatically, so a stale entry here is
/// harmless but should be cleaned up opportunistically). No NEW file should ever need to be added to
/// this list — a brand new endpoint should be written against <c>ProblemResults.FromError</c> from the
/// start.</description></item>
/// </list>
///
/// To extend either list: append the endpoint's path (relative to the repo root, forward slashes,
/// exactly as produced by enumerating <c>src/Modules/**/Features/**/Endpoint.cs</c>) together with a
/// comment explaining why it belongs there.
/// </summary>
public class ProblemResultsAdoptionArchitectureTests
{
    /// <summary>
    /// Endpoints permitted to keep manual <c>Error.Code</c> mapping indefinitely because they match a
    /// documented exception category from <see cref="HR.SharedKernel.ProblemResults"/>.
    /// </summary>
    private static readonly string[] DocumentedExceptionFiles =
    [
        // Ticket 20: bound to a pre-existing, separately-tested 422 status-code contract
        // (DataImportHardeningEndpointTests) that differs from ProblemResults' mapping. Has its own
        // inline "Ticket 20:" comment in the endpoint explaining the opt-out.
        "src/Modules/HR.Modules.DataImport/Features/ValidateImportSession/Endpoint.cs",
    ];

    /// <summary>
    /// Pre-existing manual-mapping endpoints not yet migrated to <c>ProblemResults.FromError</c> under
    /// Ticket 20's in-progress, separately tracked rollout. Do not add NEW entries here — a new
    /// endpoint should call <c>ProblemResults.FromError</c> from the start. Entries should be removed
    /// as endpoints are migrated.
    /// </summary>
    private static readonly string[] BaselineUnmigratedManualMappingFiles =
    [
        "src/Modules/HR.Modules.Assets/Features/CreateAssetAssignment/Endpoint.cs",
        "src/Modules/HR.Modules.Assets/Features/DeactivateAssetCategory/Endpoint.cs",
        "src/Modules/HR.Modules.Assets/Features/RequestAssetReturn/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/CancelCustomerDeletion/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/CreatePublicHoliday/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/ExecuteCustomerDeletion/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/ExtendCustomerTrial/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/ForceCustomerReadOnly/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/GenerateSupportSession/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/GetApplicationMetrics/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/GetAuditLog/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/GetCustomerDashboard/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/GetDeletionQueue/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/GetFailedPayments/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/GetSubscriptionStatus/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/GetSystemHealth/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/LiftCompanyLegalHold/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/ListBackgroundJobs/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/ListCustomers/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/PlaceCompanyLegalHold/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/ReinstateCustomerSubscription/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/ResumeCustomerService/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/ResumeSubscription/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/RetryBackgroundJob/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/RevokeSupportSession/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/ScheduleCustomerDeletion/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/UpdatePlatformSettings/Endpoint.cs",
        "src/Modules/HR.Modules.Companies/Features/UpdateSubscriptionPricingConfig/Endpoint.cs",
        "src/Modules/HR.Modules.CompanyOnboarding/Features/DismissOnboardingChecklist/Endpoint.cs",
        "src/Modules/HR.Modules.CompanyOnboarding/Features/GetOnboardingChecklist/Endpoint.cs",
        "src/Modules/HR.Modules.DataImport/Features/ConfirmImportSession/Endpoint.cs",
        "src/Modules/HR.Modules.DataImport/Features/ExportImportErrors/Endpoint.cs",
        "src/Modules/HR.Modules.DataImport/Features/GetImportPreview/Endpoint.cs",
        "src/Modules/HR.Modules.DataImport/Features/GetImportSession/Endpoint.cs",
        "src/Modules/HR.Modules.DataImport/Features/GetImportSessionColumns/Endpoint.cs",
        "src/Modules/HR.Modules.DataImport/Features/UploadImportFile/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/AcknowledgeSharedCompanyDocument/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/ApproveProfilePhoto/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/ArchiveSharedCompanyDocument/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/CancelDocumentRequest/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/CancelPendingProfilePhoto/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/CompleteSharedCompanyDocumentReview/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/CreateCompanyDocumentCategory/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/DeactivateCompanyDocumentCategory/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/DeactivateDocumentType/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/DeleteEmployeeDocument/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/ExpireSharedCompanyDocument/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/GetSharedCompanyDocumentAcknowledgementProgress/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/PublishSharedCompanyDocument/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/PurgeEligibleArchivedEmployeeDocuments/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/ReissueSharedCompanyDocumentAcknowledgement/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/RejectProfilePhoto/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/RequestAdditionalEmployeeDocument/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/RestoreEmployeeDocument/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/UpdateSharedCompanyDocumentAcknowledgementSettings/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/UpdateSharedCompanyDocumentAudience/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/UpdateSharedCompanyDocumentMetadata/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/UploadEmployeeDocument/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/UploadEmployeeDocumentVersion/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/UploadEmployeeProfilePhoto/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/UploadMyProfilePhoto/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/UploadRequestedDocument/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/UploadSharedCompanyDocument/Endpoint.cs",
        "src/Modules/HR.Modules.Documents/Features/UploadSharedCompanyDocumentVersion/Endpoint.cs",
        "src/Modules/HR.Modules.Employees/Features/BackfillEmployeeTimeline/Endpoint.cs",
        "src/Modules/HR.Modules.Employees/Features/CreateOnboardingTemplate/Endpoint.cs",
        "src/Modules/HR.Modules.Employees/Features/DeactivateDepartment/Endpoint.cs",
        "src/Modules/HR.Modules.Employees/Features/DeactivateEmploymentType/Endpoint.cs",
        "src/Modules/HR.Modules.Employees/Features/DeactivateLocation/Endpoint.cs",
        "src/Modules/HR.Modules.Employees/Features/DeactivateLocationType/Endpoint.cs",
        "src/Modules/HR.Modules.Employees/Features/DeactivateOnboardingTemplate/Endpoint.cs",
        "src/Modules/HR.Modules.Employees/Features/DeactivatePositionProfile/Endpoint.cs",
        "src/Modules/HR.Modules.Employees/Features/RemoveOnboardingTemplateFromPositionProfile/Endpoint.cs",
        "src/Modules/HR.Modules.Employees/Features/RemoveRequiredAssetFromPositionProfile/Endpoint.cs",
        "src/Modules/HR.Modules.Employees/Features/RemoveRequiredDocumentFromPositionProfile/Endpoint.cs",
        "src/Modules/HR.Modules.Employees/Features/RequestPersonalDetailsChange/Endpoint.cs",
        "src/Modules/HR.Modules.Employees/Features/SaveMyEqualityData/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/AddEmployeeRoleOverride/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/AssignPlatformAdministratorRole/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/CancelInvite/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/CreatePlatformAdministrator/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/DisablePlatformAdministrator/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/DisableUser/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/EnablePlatformAdministrator/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/EnableUser/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/GetUserAuditHistory/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/InviteEmployeeUser/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/ListPlatformAdministrators/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/RemoveEmployeeRoleOverride/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/ResendInvite/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/ResetPlatformAdministratorMfa/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/ResetPlatformAdministratorPassword/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/SetPositionRoleDefaults/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/SignUp/Endpoint.cs",
        "src/Modules/HR.Modules.Identity/Features/UpdateUserRoles/Endpoint.cs",
        "src/Modules/HR.Modules.Leave/Features/CreateLeavePolicy/Endpoint.cs",
        "src/Modules/HR.Modules.Leave/Features/DeactivateLeaveType/Endpoint.cs",
        "src/Modules/HR.Modules.Leave/Features/SetDefaultLeavePolicy/Endpoint.cs",
        "src/Modules/HR.Modules.Leave/Features/SubmitLeaveRequestDraft/Endpoint.cs",
        "src/Modules/HR.Modules.Marketing/Features/CreateMarketingFeature/Endpoint.cs",
        "src/Modules/HR.Modules.Marketing/Features/ReorderMarketingFeatures/Endpoint.cs",
        "src/Modules/HR.Modules.Marketing/Features/ReorderMarketingRoadmapItems/Endpoint.cs",
        "src/Modules/HR.Modules.Marketing/Features/SetMarketingFeaturePublication/Endpoint.cs",
        "src/Modules/HR.Modules.Marketing/Features/SetMarketingRoadmapItemPublication/Endpoint.cs",
        "src/Modules/HR.Modules.Marketing/Features/UpdateMarketingRoadmapItem/Endpoint.cs",
        "src/Modules/HR.Modules.Notifications/Features/GetOperationalAlert/Endpoint.cs",
        "src/Modules/HR.Modules.Notifications/Features/ListOperationalAlerts/Endpoint.cs",
        "src/Modules/HR.Modules.Notifications/Features/ResolveOperationalAlert/Endpoint.cs",
        "src/Modules/HR.Modules.Offboarding/Features/StartOffboarding/Endpoint.cs",
        "src/Modules/HR.Modules.Probation/Features/CompleteProbationReview/Endpoint.cs",
        "src/Modules/HR.Modules.Probation/Features/CreateProbationRecord/Endpoint.cs",
        "src/Modules/HR.Modules.Probation/Features/CreateProbationReview/Endpoint.cs",
        "src/Modules/HR.Modules.Probation/Features/GetProbationRecordByEmployee/Endpoint.cs",
        "src/Modules/HR.Modules.Probation/Features/MarkProbationNotApplicable/Endpoint.cs",
        "src/Modules/HR.Modules.Recruitment/Features/CreateCandidate/Endpoint.cs",
        "src/Modules/HR.Modules.Recruitment/Features/CreateExternalRecruiter/Endpoint.cs",
        "src/Modules/HR.Modules.Recruitment/Features/CreateRecruitmentStage/Endpoint.cs",
        "src/Modules/HR.Modules.Recruitment/Features/DeactivateCandidate/Endpoint.cs",
        "src/Modules/HR.Modules.Recruitment/Features/GetInternalVacancy/Endpoint.cs",
        "src/Modules/HR.Modules.Recruitment/Features/PurgeEligibleCandidates/Endpoint.cs",
        "src/Modules/HR.Modules.Recruitment/Features/ReactivateCandidate/Endpoint.cs",
        "src/Modules/HR.Modules.Recruitment/Features/SetExternalRecruiterActiveStatus/Endpoint.cs",
        "src/Modules/HR.Modules.Recruitment/Features/SetRecruitmentStageActiveStatus/Endpoint.cs",
        "src/Modules/HR.Modules.Recruitment/Features/UpdateCandidate/Endpoint.cs",
        "src/Modules/HR.Modules.Recruitment/Features/UpdateExternalRecruiter/Endpoint.cs",
        "src/Modules/HR.Modules.Recruitment/Features/UpdateInterview/Endpoint.cs",
        "src/Modules/HR.Modules.Recruitment/Features/UpdateRecruitmentStage/Endpoint.cs",
        "src/Modules/HR.Modules.Recruitment/Features/UpdateVacancy/Endpoint.cs",
        "src/Modules/HR.Modules.Recruitment/Features/UploadCandidateDocument/Endpoint.cs",
        "src/Modules/HR.Modules.Reporting/Features/AddReportFavourite/Endpoint.cs",
        "src/Modules/HR.Modules.Reporting/Features/DeleteReportView/Endpoint.cs",
        "src/Modules/HR.Modules.Reporting/Features/RenameReportView/Endpoint.cs",
        "src/Modules/HR.Modules.Reporting/Features/RequestOrganisationDataExport/Endpoint.cs",
        "src/Modules/HR.Modules.Reporting/Features/SaveReportView/Endpoint.cs",
        "src/Modules/HR.Modules.Reporting/Features/SetDefaultReportView/Endpoint.cs",
        "src/Modules/HR.Modules.Sickness/Features/CloseSicknessRecord/Endpoint.cs",
        "src/Modules/HR.Modules.Sickness/Features/CompleteReturnToWorkReview/Endpoint.cs",
        "src/Modules/HR.Modules.Sickness/Features/DeactivateSicknessCategory/Endpoint.cs",
        "src/Modules/HR.Modules.Sickness/Features/RecordMySickness/Endpoint.cs",
        "src/Modules/HR.Modules.Sickness/Features/RecordSickness/Endpoint.cs",
        "src/Modules/HR.Modules.Tasks/Features/CompleteTask/Endpoint.cs",
        "src/Modules/HR.Modules.Tasks/Features/GetTask/Endpoint.cs",
        "src/Modules/HR.Modules.Tasks/Features/ReassignTask/Endpoint.cs",
    ];

    private const string ManualMappingMarker = "result.Error.Code ==";
    private const string TranslatorMarker = "ProblemResults.FromError(";

    [Fact]
    public void No_New_Endpoint_Introduces_Manual_ErrorCode_Mapping_Outside_The_Documented_Baseline()
    {
        var repoRoot = FindRepoRoot();
        var modulesDir = Path.Combine(repoRoot, "src", "Modules");

        Assert.True(Directory.Exists(modulesDir),
            $"Expected to find 'src/Modules' under repo root '{repoRoot}' — repo root detection is broken.");

        var endpointFiles = Directory.EnumerateFiles(modulesDir, "Endpoint.cs", SearchOption.AllDirectories)
            .Where(f => f.Replace('\\', '/').Contains("/Features/"))
            .ToList();

        Assert.True(endpointFiles.Count > 0,
            "Expected to find at least one Endpoint.cs under src/Modules/**/Features/** — the file " +
            "enumeration is probably broken.");

        var allowed = new HashSet<string>(DocumentedExceptionFiles.Concat(BaselineUnmigratedManualMappingFiles),
            StringComparer.OrdinalIgnoreCase);

        var newViolations = new List<string>();
        var danglingManualCodeAlongsideTranslator = new List<string>();

        foreach (var absolutePath in endpointFiles)
        {
            var relativePath = Path.GetRelativePath(repoRoot, absolutePath).Replace('\\', '/');
            var text = File.ReadAllText(absolutePath);

            var hasManualMapping = text.Contains(ManualMappingMarker, StringComparison.Ordinal);
            var hasTranslator = text.Contains(TranslatorMarker, StringComparison.Ordinal);

            if (hasManualMapping && hasTranslator)
            {
                // Leftover dead manual-mapping code sitting alongside the new translator call —
                // never allow-listed, always a violation so it gets cleaned up.
                danglingManualCodeAlongsideTranslator.Add(relativePath);
                continue;
            }

            if (!hasManualMapping || hasTranslator)
            {
                continue;
            }

            if (!allowed.Contains(relativePath))
            {
                newViolations.Add(relativePath);
            }
        }

        Assert.True(danglingManualCodeAlongsideTranslator.Count == 0,
            "Endpoints contain BOTH manual 'result.Error.Code ==' branching AND a " +
            "'ProblemResults.FromError(' call — this is leftover dead manual-mapping code that should " +
            "be removed as part of the ProblemResults.FromError migration:" +
            Environment.NewLine + string.Join(Environment.NewLine, danglingManualCodeAlongsideTranslator));

        Assert.True(newViolations.Count == 0,
            "Found endpoint(s) with manual 'result.Error.Code ==' mapping that are not on the " +
            "documented exception list or the pre-existing unmigrated baseline (Ticket 20). New " +
            "endpoints must use HR.SharedKernel.ProblemResults.FromError(result.Error) instead of " +
            "hand-rolled status-code branching. If this is a genuine, deliberate exception (matching " +
            "one of the categories documented on HR.SharedKernel.ProblemResults), add it to " +
            "DocumentedExceptionFiles with a comment explaining why:" +
            Environment.NewLine + string.Join(Environment.NewLine, newViolations));
    }

    /// <summary>
    /// Walks up from the test assembly's location until it finds a directory containing both
    /// <c>src/Modules</c> and a <c>.sln</c>/<c>.slnx</c> file, which identifies the repository root.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "Modules"))
                && (dir.GetFiles("*.sln").Length > 0 || dir.GetFiles("*.slnx").Length > 0))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root by walking up from '{AppContext.BaseDirectory}'.");
    }
}
