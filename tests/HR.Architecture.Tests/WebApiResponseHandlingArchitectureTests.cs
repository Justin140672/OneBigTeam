namespace HR.Architecture.Tests;

/// <summary>
/// Enforces adoption of the shared HTTP response contracts/reader
/// (<see cref="HR.SharedKernel.Http.ApiResult{T}"/>, <see cref="HR.SharedKernel.Http.ApiResponseReader"/>,
/// <see cref="HR.SharedKernel.Http.ApiErrorEnvelope"/>, <see cref="HR.SharedKernel.Http.ApiValidationEnvelope"/>)
/// across HR.Web and HR.Admin.Web service classes, in place of the many bespoke local
/// <c>ErrorEnvelope</c>/<c>ValidationErrorEnvelope</c> records and <c>TryDeserialize</c> helpers that
/// previously disagreed about status-code handling, validation-error flattening and malformed-body
/// tolerance.
///
/// This test does not require the whole backlog to be migrated in one pass (it spans ~90 files). It
/// pins a baseline of pre-existing offenders so the list can only shrink, never grow: any NEW service
/// file introducing a local error envelope, a local TryDeserialize helper, or a broad
/// "catch { return null/false/[]; }" swallow is rejected outright.
/// </summary>
public class WebApiResponseHandlingArchitectureTests
{
    /// <summary>
    /// Pre-existing service files (as of the shared-response-reader migration) that still define a
    /// local error/validation envelope record or a local TryDeserialize helper instead of the shared
    /// HR.SharedKernel.Http types. Entries should be removed as each file is migrated. Do not add new
    /// entries — a new or newly-touched service should use the shared contracts from the start.
    /// </summary>
    private static readonly string[] BaselineLocalEnvelopeFiles =
    [
        "src/HR.Web/Services/ApplicationService.cs",
        "src/HR.Web/Services/CompanyService.cs",
        "src/HR.Web/Services/CompensationService.cs",
        "src/HR.Web/Services/DepartmentService.cs",
        "src/HR.Web/Services/DocumentTypeService.cs",
        "src/HR.Web/Services/EmployeeNoteService.cs",
        "src/HR.Web/Services/EmployeeTimelineService.cs",
        "src/HR.Web/Services/EmploymentTypeService.cs",
        "src/HR.Web/Services/ExternalRecruiterService.cs",
        "src/HR.Web/Services/InviteService.cs",
        "src/HR.Web/Services/LocationService.cs",
        "src/HR.Web/Services/LocationTypeService.cs",
        "src/HR.Web/Services/OnboardingTemplateService.cs",
        "src/HR.Web/Services/PositionProfileService.cs",
        "src/HR.Web/Services/PromotionService.cs",
        "src/HR.Web/Services/PublicHolidayService.cs",
        "src/HR.Web/Services/ReportingService.cs",
        "src/HR.Web/Services/SicknessCategoryService.cs",
        // (EmployeeService.cs, DocumentService.cs, AssetCategoryService.cs, AssetService.cs,
        // CandidateService.cs, InterviewService.cs, RecruitmentKanbanService.cs,
        // RecruitmentStageService.cs, VacancyService.cs, ProbationService.cs, LeaveService.cs,
        // LeaveTypeService.cs, LeavePolicyService.cs, UserAdministrationService.cs,
        // SupportService.cs migrated — their remaining local envelopes/TryDeserialize helpers were
        // removed as part of this pass; they are intentionally NOT in this list.)
        "src/HR.Admin.Web/Services/MarketingContentAdminService.cs",
        "src/HR.Admin.Web/Services/PlatformSettingsService.cs",
        "src/HR.Admin.Web/Services/SubscriptionPricingService.cs",
    ];

    private static readonly string[] LocalEnvelopeMarkers =
    [
        "record ErrorEnvelope",
        "record ValidationErrorEnvelope",
        "record ValidationErrorResponse",
        "static T? TryDeserialize<T>",
    ];

    [Fact]
    public void No_New_Web_Service_File_Introduces_A_Local_Error_Envelope_Or_TryDeserialize_Helper()
    {
        var repoRoot = FindRepoRoot();
        var serviceFiles = EnumerateServiceFiles(repoRoot);

        var allowed = new HashSet<string>(BaselineLocalEnvelopeFiles, StringComparer.OrdinalIgnoreCase);
        var newViolations = new List<string>();

        foreach (var (relativePath, text) in serviceFiles)
        {
            var hasLocalEnvelope = LocalEnvelopeMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));

            if (!hasLocalEnvelope)
                continue;

            if (!allowed.Contains(relativePath))
                newViolations.Add(relativePath);
        }

        Assert.True(newViolations.Count == 0,
            "Found service file(s) with a local ErrorEnvelope/ValidationErrorEnvelope record or a " +
            "local TryDeserialize<T> helper that are not on the documented pre-existing baseline. New " +
            "or newly-touched service methods must use the shared HR.SharedKernel.Http.ApiResult<T> / " +
            "ApiResponseReader / ApiErrorEnvelope / ApiValidationEnvelope contracts instead of a " +
            "bespoke local envelope:" + Environment.NewLine + string.Join(Environment.NewLine, newViolations));
    }

    /// <summary>
    /// Pre-existing service files that still swallow a failed read into null/false/an empty collection
    /// via a broad "catch { return ...; }" (or "catch (Exception ...) { return ...; }") with no
    /// distinction for network failure vs. a genuine empty result. Entries should be removed as each
    /// file migrates its read paths to ApiResponseReader. Do not add new entries.
    /// </summary>
    private static readonly string[] BaselineBroadCatchFiles =
    [
        "src/HR.Admin.Web/Services/SupportRequestAdminService.cs",
        "src/HR.Web/Services/DataImportService.cs",
        // Write paths migrated to ApiResponseReader; several read (GET) methods still swallow into
        // null via a bare catch — tracked as follow-up work, not yet migrated.
        "src/HR.Web/Services/DocumentService.cs",
        "src/HR.Web/Services/EmployeeService.cs",
        "src/HR.Web/Services/NotificationService.cs",
        "src/HR.Web/Services/ProfilePhotoService.cs",
        "src/HR.Web/Services/SicknessService.cs",
        "src/HR.Web/Services/TaskService.cs",
        "src/HR.Web/Services/ApplicationService.cs",
        "src/HR.Web/Services/AppSession.cs",
        "src/HR.Web/Services/AppSessionAuthStateProvider.cs",
        "src/HR.Web/Services/AuditHistoryService.cs",
        "src/HR.Web/Services/CompensationService.cs",
        "src/HR.Web/Services/EmployeeNoteService.cs",
        "src/HR.Web/Services/EmployeeTimelineService.cs",
        "src/HR.Web/Services/ExternalRecruiterService.cs",
        "src/HR.Web/Services/OffboardingService.cs",
        "src/HR.Web/Services/OnboardingService.cs",
        "src/HR.Web/Services/OrganisationChartService.cs",
        "src/HR.Web/Services/PositionProfileService.cs",
        "src/HR.Web/Services/PromotionService.cs",
        "src/HR.Web/Services/ReportingService.cs",
        "src/HR.Web/Services/SupabaseSessionAccessor.cs",
        "src/HR.Admin.Web/Services/MarketingContentAdminService.cs",
        "src/HR.Admin.Web/Services/SupabaseSessionAccessor.cs",
    ];

    [Fact]
    public void No_New_Web_Service_File_Introduces_A_Broad_Catch_That_Discards_The_Exception()
    {
        var repoRoot = FindRepoRoot();
        var serviceFiles = EnumerateServiceFiles(repoRoot);

        var allowed = new HashSet<string>(BaselineBroadCatchFiles, StringComparer.OrdinalIgnoreCase);
        var newViolations = new List<string>();

        foreach (var (relativePath, text) in serviceFiles)
        {
            // Detects "catch { ... }" (no exception variable at all — cannot possibly re-throw
            // cancellation) as the narrowest, lowest-false-positive heuristic for this rule. Broad
            // "catch (Exception ex) { return ...; }" without re-throwing cancellation is the harder,
            // file-by-file migration tracked via BaselineLocalEnvelopeFiles/the ticket follow-up list
            // rather than enforced here to avoid flagging legitimate non-API exception handling.
            var hasBareCatch = System.Text.RegularExpressions.Regex.IsMatch(text, @"catch\s*\{");

            if (!hasBareCatch)
                continue;

            if (!allowed.Contains(relativePath))
                newViolations.Add(relativePath);
        }

        Assert.True(newViolations.Count == 0,
            "Found service file(s) with a bare 'catch { ... }' block that unconditionally discards the " +
            "exception (including OperationCanceledException) and converts a failed API call into a " +
            "default value. Use HR.SharedKernel.Http.ApiResponseReader.ExecuteAsync (or handle the " +
            "specific exception types you need) instead so cancellation propagates and network failures " +
            "are distinguishable from a genuine empty result:" +
            Environment.NewLine + string.Join(Environment.NewLine, newViolations));
    }

    private static List<(string RelativePath, string Text)> EnumerateServiceFiles(string repoRoot)
    {
        var results = new List<(string, string)>();

        foreach (var servicesDir in new[]
                 {
                     Path.Combine(repoRoot, "src", "HR.Web", "Services"),
                     Path.Combine(repoRoot, "src", "HR.Admin.Web", "Services"),
                 })
        {
            if (!Directory.Exists(servicesDir))
                continue;

            foreach (var absolutePath in Directory.EnumerateFiles(servicesDir, "*.cs", SearchOption.TopDirectoryOnly))
            {
                var relativePath = Path.GetRelativePath(repoRoot, absolutePath).Replace('\\', '/');
                results.Add((relativePath, File.ReadAllText(absolutePath)));
            }
        }

        Assert.True(results.Count > 0,
            "Expected to find at least one service file under src/HR.Web/Services or " +
            "src/HR.Admin.Web/Services — the file enumeration is probably broken.");

        return results;
    }

    /// <summary>
    /// Walks up from the test assembly's location until it finds a directory containing both
    /// <c>src/HR.Web</c> and a <c>.sln</c>/<c>.slnx</c> file, which identifies the repository root.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "HR.Web"))
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
