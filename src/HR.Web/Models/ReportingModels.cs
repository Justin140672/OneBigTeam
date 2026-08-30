namespace HR.Web.Models;

// ── Catalog ───────────────────────────────────────────────────────────────────

public record GetReportCatalogResponse(List<ReportCatalogItemModel> Items);

public record ReportCatalogItemModel(
    string Id,
    string DisplayName,
    string Category,
    string Description);

/// <summary>
/// Catalog report id -> the route segment for its dedicated page under
/// /companies/{companyId}/reporting/{route}. Shared between ReportCatalogPage (main catalog grid)
/// and FavouriteReportsWidget/TeamReportsWidget (dashboard widgets) so the two never drift apart —
/// see ReportCatalogPage's "phase 1 proved the permission-filtered pattern" comment for why some
/// catalog entries have no route yet ("Coming soon").
/// </summary>
public static class ReportRoutes
{
    public static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
    {
        ["employee-directory"] = "employee-directory",
        ["employee-starters"] = "employee-starters",
        ["employee-leavers"] = "employee-leavers",
        ["leave-summary"] = "leave-summary",
        ["leave-calendar"] = "leave-calendar",
        ["sickness-report"] = "sickness",
        ["recruitment-pipeline-report"] = "recruitment-pipeline",
        ["vacancy-performance-report"] = "vacancy-performance",
        ["probation-report"] = "probation",
        ["onboarding-progress"] = "onboarding-progress",
        ["offboarding-progress"] = "offboarding-progress",
        ["document-compliance"] = "document-compliance",
        ["document-acknowledgement"] = "document-acknowledgement",
        ["asset-assignment"] = "asset-assignment",
        ["workload-actions"] = "workload-actions",
        ["recruitment-pipeline-summary"] = "recruitment-pipeline-summary",
        ["hr-headcount-summary"] = "hr-headcount-summary",
    };

    public static bool IsClickable(string reportId) => Map.ContainsKey(reportId);

    public static string? RouteFor(string reportId) => Map.GetValueOrDefault(reportId);
}

// ── Employee Directory report ────────────────────────────────────────────────

public record EmployeeDirectoryReportFilter(
    Guid? DepartmentId = null,
    Guid? LocationId = null,
    Guid? PositionProfileId = null,
    Guid? ManagerId = null,
    Guid? EmploymentTypeId = null,
    DateOnly? DateRangeStart = null,
    DateOnly? DateRangeEnd = null,
    string? EmployeeStatus = null,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    bool SortDescending = false);

public record GetEmployeeDirectoryReportResponse(
    List<EmployeeDirectoryReportItemModel> Items,
    int TotalCount,
    int Page,
    int PageSize);

public record EmployeeDirectoryReportItemModel(
    Guid EmployeeId,
    string EmployeeNumber,
    string Name,
    string? Department,
    string? Position,
    string? Manager,
    string? EmploymentType,
    DateOnly StartDate,
    string Status,
    string? WorkLocation,
    string Email);

// ── Shared report filter criteria (Department/Location/PositionProfile/EmploymentType/DateRange) ──

public record ReportFilterCriteriaModel(
    Guid? DepartmentId = null,
    Guid? LocationId = null,
    Guid? PositionProfileId = null,
    Guid? EmploymentTypeId = null,
    DateOnly? DateRangeStart = null,
    DateOnly? DateRangeEnd = null);

// ── Employee Starter report ──────────────────────────────────────────────────

public record EmployeeStarterReportFilter(
    Guid? DepartmentId = null,
    Guid? LocationId = null,
    Guid? PositionProfileId = null,
    Guid? EmploymentTypeId = null,
    DateOnly? DateRangeStart = null,
    DateOnly? DateRangeEnd = null,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    bool SortDescending = false);

public record GetEmployeeStarterReportResponse(
    List<EmployeeStarterReportItemModel> Items,
    int TotalCount,
    int Page,
    int PageSize);

public record EmployeeStarterReportItemModel(
    Guid EmployeeId,
    string Name,
    DateOnly StartDate,
    string? Recruiter,
    string? Department,
    string? Position,
    string? OnboardingStatus,
    string? ProbationStatus);

// ── Employee Leaver report ───────────────────────────────────────────────────

public record EmployeeLeaverReportFilter(
    Guid? DepartmentId = null,
    Guid? PositionProfileId = null,
    DateOnly? DateRangeStart = null,
    DateOnly? DateRangeEnd = null,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    bool SortDescending = false);

public record GetEmployeeLeaverReportResponse(
    List<EmployeeLeaverReportItemModel> Items,
    int TotalCount,
    int Page,
    int PageSize);

public record EmployeeLeaverReportItemModel(
    Guid EmployeeId,
    string Name,
    DateOnly? LeavingDate,
    DateOnly? LastWorkingDay,
    string? Department,
    string? Position,
    string? Reason,
    string? OffboardingStatus,
    string AccountStatus);

// ── Leave Summary report ─────────────────────────────────────────────────────

public enum LeaveSummaryGroupBy
{
    Employee,
    Department,
    LeaveType,
}

public record LeaveSummaryReportFilter(
    int PolicyYear,
    Guid? DepartmentId,
    LeaveSummaryGroupBy GroupBy,
    Guid? LeaveTypeId = null);

public record GetLeaveSummaryReportResponse(List<LeaveSummaryGroupRowModel> Items);

public record LeaveSummaryGroupRowModel(
    string GroupKey,
    string GroupLabel,
    decimal EntitlementDays,
    decimal BookedDays,
    decimal ApprovedDays,
    decimal RemainingDays,
    int PendingRequestCount);

// ── Leave Calendar report ────────────────────────────────────────────────────

public record LeaveCalendarReportFilter(
    int Year,
    int Month,
    Guid? DepartmentId);

public record GetLeaveCalendarReportResponse(List<LeaveCalendarReportRowModel> Items);

public record LeaveCalendarReportRowModel(
    Guid EmployeeId,
    string EmployeeName,
    string? Department,
    DateOnly LeaveStart,
    DateOnly LeaveEnd,
    string LeaveTypeName,
    decimal DurationDays,
    string ApprovalStatus);

// ── Sickness report ───────────────────────────────────────────────────────────

public enum SicknessReportGroupBy
{
    Employee = 1,
    Department = 2,
}

public record SicknessReportFilter(
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    SicknessReportGroupBy GroupBy = SicknessReportGroupBy.Employee);

public record GetSicknessReportResponse(List<SicknessReportGroupRowModel> Items);

public record SicknessReportGroupRowModel(
    string GroupKey,
    string GroupLabel,
    int AbsenceCount,
    decimal DaysAbsent,
    int BradfordScore);

// ── Recruitment Pipeline report ─────────────────────────────────────────────

public enum RecruitmentPipelineGroupBy
{
    Recruiter = 1,
    Vacancy = 2,
}

public record RecruitmentPipelineReportFilter(
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    RecruitmentPipelineGroupBy GroupBy = RecruitmentPipelineGroupBy.Recruiter);

public record GetRecruitmentPipelineReportResponse(List<RecruitmentPipelineReportRowModel> Items);

public record RecruitmentPipelineReportRowModel(
    string GroupKey,
    string GroupLabel,
    int Vacancies,
    int Applicants,
    int Interviews,
    int Offers,
    int Hires);

// ── Vacancy Performance report ──────────────────────────────────────────────

public record VacancyPerformanceReportFilter(
    DateOnly? StartDate = null,
    DateOnly? EndDate = null);

public record GetVacancyPerformanceReportResponse(List<VacancyPerformanceReportRowModel> Items);

public record VacancyPerformanceReportRowModel(
    Guid VacancyId,
    string VacancyTitle,
    int DaysOpen,
    int ApplicantCount,
    int InterviewCount,
    int OfferCount,
    DateOnly? HireDate);

// ── Probation report ─────────────────────────────────────────────────────────

public record GetProbationReportResponse(
    List<ProbationReportRowModel> Items,
    int CurrentProbationCount,
    int DueReviewCount,
    int OverdueReviewCount,
    int PassedCount,
    int ExtendedCount);

public record ProbationReportRowModel(
    Guid EmployeeId,
    string EmployeeName,
    string Status,
    DateOnly StartDate,
    DateOnly ExpectedEndDate,
    int DueReviews,
    int OverdueReviews);

// ── Onboarding Progress report ───────────────────────────────────────────────

public record OnboardingProgressReportFilter(bool OverdueOnly = false);

public record GetOnboardingProgressReportResponse(
    List<OnboardingProgressReportRowModel> Items,
    int TotalEmployees,
    int TotalOutstandingTasks,
    int OverdueEmployeeCount);

public record OnboardingProgressReportRowModel(
    Guid EmployeeId,
    string EmployeeName,
    string PlanStatus,
    int ProgressPercent,
    List<OnboardingReportTaskItemModel> OutstandingTasks,
    bool HasOverdueTasks);

public record OnboardingReportTaskItemModel(
    string Title,
    DateOnly? DueDate,
    string? Owner,
    bool IsOverdue);

// ── Offboarding Progress report ──────────────────────────────────────────────

public record GetOffboardingProgressReportResponse(
    List<OffboardingProgressReportRowModel> Items,
    int TotalEmployees,
    int OutstandingAccessCount,
    int OutstandingAssetsCount);

public record OffboardingProgressReportRowModel(
    Guid EmployeeId,
    string EmployeeName,
    DateOnly LastWorkingDay,
    string Status,
    List<string> OutstandingTasks,
    List<string> CompletedTasks,
    bool AccessDisabled,
    bool DocumentsReturned,
    bool AssetsReturned);

// ── Document Compliance report ───────────────────────────────────────────────

public record DocumentComplianceReportFilter(Guid? PositionProfileId = null);

public record GetDocumentComplianceReportResponse(
    List<DocumentComplianceReportRowModel> Items,
    int TotalEmployees,
    int TotalMissing,
    int TotalExpiringSoon,
    int TotalExpired);

public record DocumentComplianceReportRowModel(
    Guid EmployeeId,
    string EmployeeName,
    int RequiredCount,
    int UploadedCount,
    int MissingCount,
    int ExpiringSoonCount,
    int ExpiredCount,
    List<string> MissingDocumentTypeNames);

// ── Company Document Acknowledgement report ──────────────────────────────────

public record GetCompanyDocumentAcknowledgementReportResponse(
    List<CompanyDocumentAcknowledgementReportRowModel> Items,
    int TotalRequired,
    int TotalAcknowledged,
    int TotalOutstanding);

public record CompanyDocumentAcknowledgementReportRowModel(
    string DocumentTitle,
    Guid EmployeeId,
    string EmployeeName,
    bool Acknowledged,
    DateTimeOffset? AcknowledgedAt);

// ── Asset Assignment report ──────────────────────────────────────────────────

public record GetAssetAssignmentReportResponse(
    List<AssetAssignmentReportRowModel> Items,
    int TotalAssignments);

public record AssetAssignmentReportRowModel(
    Guid EmployeeId,
    string EmployeeName,
    string AssetName,
    string? SerialNumber,
    DateTimeOffset AssignedDate,
    string ReturnStatus);

// ── Workload & HR Actions report ─────────────────────────────────────────────

public enum WorkloadActionsGroupBy
{
    ActionType,
    AssignedUser,
    Department,
    DueDate,
}

public record WorkloadActionsReportFilter(
    string? ActionType = null,
    string? Department = null,
    string? Urgency = null,
    string? Status = null,
    Guid? EmployeeId = null,
    DateOnly? DueDateStart = null,
    DateOnly? DueDateEnd = null,
    WorkloadActionsGroupBy? GroupBy = null,
    Guid? ManagerId = null,
    Guid? LocationId = null,
    string? RecruitmentUser = null);

public record GetWorkloadActionsResponse(
    List<WorkloadActionRowModel> Items,
    List<WorkloadActionGroupModel> Groups,
    WorkloadActionSummaryModel Summary);

public record WorkloadActionRowModel(
    Guid EmployeeId,
    string EmployeeName,
    string? Department,
    string ActionType,
    string ActionCategory,
    DateOnly? DueDate,
    string? AssignedTo,
    string Status,
    string Urgency,
    string DeepLinkUrl);

public record WorkloadActionGroupModel(
    string Key,
    List<WorkloadActionRowModel> Items);

public record WorkloadActionSummaryModel(
    int TotalOutstanding,
    int Overdue,
    int DueToday,
    int DueThisWeek);

// ── Recruitment Pipeline Summary report ─────────────────────────────────────

public record RecruitmentPipelineSummaryReportFilter(bool IncludeClosed = false);

public record GetRecruitmentPipelineSummaryReportResponse(
    List<RecruitmentPipelineSummaryRowModel> Vacancies,
    List<RecruitmentStageColumnModel> Stages);

public record RecruitmentStageColumnModel(Guid StageId, string StageName);

public record RecruitmentPipelineSummaryRowModel(
    Guid VacancyId,
    string VacancyTitle,
    string? PositionProfileTitle,
    string? DepartmentName,
    string Status,
    DateOnly? OpenedAt,
    int CandidateCount,
    Dictionary<Guid, int> CandidatesByStage);

// ── HR Headcount Summary report ──────────────────────────────────────────────

public record HrHeadcountSummaryReportFilter(
    Guid? DepartmentId = null,
    Guid? LocationId = null,
    Guid? EmploymentTypeId = null,
    string? EmployeeStatus = null);

public record GetHrHeadcountSummaryReportResponse(
    List<HrHeadcountSummaryReportItemModel> Items,
    int TotalHeadcount,
    int ActiveEmployees,
    int FutureStarters,
    int Leavers,
    decimal TotalFte);

public record HrHeadcountSummaryReportItemModel(
    Guid EmployeeId,
    string EmployeeName,
    string? Department,
    string? Location,
    string? Position,
    string? EmploymentType,
    string Status,
    DateOnly StartDate,
    DateOnly? LeavingDate,
    decimal? Fte);

// ── Compliance Centre (ADM-02) ──────────────────────────────────────────────

public record ComplianceCentreFilter(
    string? Category = null,
    string? Department = null,
    Guid? ManagerId = null,
    DateOnly? DueDateStart = null,
    DateOnly? DueDateEnd = null,
    string? Severity = null);

public record GetComplianceCentreResponse(
    List<ComplianceItemRowModel> Items,
    List<ComplianceCategorySummaryModel> CategorySummaries,
    ComplianceCentreSummaryModel Summary,
    int TotalCount,
    bool IsTruncated,
    bool NoActionRequired);

public record ComplianceItemRowModel(
    Guid EmployeeId,
    string EmployeeName,
    string? Department,
    string Category,
    string CategoryLabel,
    string Detail,
    DateOnly? DueDate,
    string Severity,
    string DeepLinkUrl);

public record ComplianceCategorySummaryModel(
    string Category,
    string CategoryLabel,
    int Total,
    int Overdue,
    int DueSoon,
    int Informational);

public record ComplianceCentreSummaryModel(
    int Total,
    int Overdue,
    int DueSoon,
    int Informational);

// ── Favourites ────────────────────────────────────────────────────────────────

public record GetReportFavouritesResponse(List<string> ReportIds);

// ── Saved Report Views ───────────────────────────────────────────────────────

public record SavedReportViewModel(
    Guid Id,
    string ReportId,
    string Name,
    string FilterCriteriaJson,
    bool IsDefault,
    DateTimeOffset CreatedAt);

public record GetReportViewsResponse(List<SavedReportViewModel> Views);

public record SaveReportViewRequest(
    string ReportId,
    string Name,
    string FilterCriteriaJson,
    bool? IsDefault);

public record SaveReportViewResponse(
    Guid Id,
    string ReportId,
    string Name,
    string FilterCriteriaJson,
    bool IsDefault,
    DateTimeOffset CreatedAt);

public record RenameReportViewRequest(string Name);

public record RenameReportViewResponse(Guid Id, string Name);

public record SetDefaultReportViewResponse(Guid Id, bool IsDefault);

public record DeleteReportViewResponse(Guid Id);
