namespace HR.Web.Models;


public record GetReportCatalogResponse(List<ReportCatalogItemModel> Items);

public record ReportCatalogItemModel(
    string Id,
    string DisplayName,
    string Category,
    string Description);

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
        ["equality-diversity"] = "equality-diversity",
        ["governance-user-activity"] = "governance/user-activity",
        ["governance-administrative-changes"] = "governance/administrative-changes",
        ["governance-compliance-status"] = "governance/compliance-status",
        ["governance-security-events"] = "governance/security-events",
    };

    public static bool IsClickable(string reportId) => Map.ContainsKey(reportId);

    public static string? RouteFor(string reportId) => Map.GetValueOrDefault(reportId);
}


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


public record ReportFilterCriteriaModel(
    Guid? DepartmentId = null,
    Guid? LocationId = null,
    Guid? PositionProfileId = null,
    Guid? EmploymentTypeId = null,
    DateOnly? DateRangeStart = null,
    DateOnly? DateRangeEnd = null);


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


public enum RecruitmentPipelineGroupBy
{
    Recruiter = 1,
    Vacancy = 2,
}

public record RecruitmentPipelineReportFilter(
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    RecruitmentPipelineGroupBy GroupBy = RecruitmentPipelineGroupBy.Recruiter,
    // Internal recruitment Ticket 6: null = all applications; true = internal only; false = external only.
    bool? IsInternal = null);

public record GetRecruitmentPipelineReportResponse(List<RecruitmentPipelineReportRowModel> Items);

public record RecruitmentPipelineReportRowModel(
    string GroupKey,
    string GroupLabel,
    int Vacancies,
    int Candidates,
    int Interviews,
    int Offers,
    int Hires);


public record VacancyPerformanceReportFilter(
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    // Internal recruitment Ticket 6: null = all applications; true = internal only; false = external only.
    bool? IsInternal = null);

public record GetVacancyPerformanceReportResponse(List<VacancyPerformanceReportRowModel> Items);

public record VacancyPerformanceReportRowModel(
    Guid VacancyId,
    string VacancyTitle,
    int DaysOpen,
    int CandidateCount,
    int InterviewCount,
    int OfferCount,
    DateOnly? HireDate);


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
    string DeepLinkUrl,
    Guid? TaskId = null,
    bool IsOwnerActionable = true,
    string? OwnerLabel = null);

public record WorkloadActionGroupModel(
    string Key,
    List<WorkloadActionRowModel> Items);

public record WorkloadActionSummaryModel(
    int TotalOutstanding,
    int Overdue,
    int DueToday,
    int DueThisWeek);


// IsInternal (internal recruitment Ticket 6): null = all applications; true = internal only; false = external only.
public record RecruitmentPipelineSummaryReportFilter(bool IncludeClosed = false, bool? IsInternal = null);

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


public record GovernanceAuditReportFilter(
    Guid? ActorUserId = null,
    string? EventType = null,
    Guid? EmployeeId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20);

public record GetGovernanceAuditReportResponse(
    List<GovernanceAuditReportRowModel> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool IsTruncated);

public record GovernanceAuditReportRowModel(
    DateTimeOffset OccurredAt,
    string EventType,
    string EntityType,
    Guid? ActorUserId,
    string? ActorEmail,
    Guid? EmployeeId,
    string Status,
    string? Summary);

public record GovernanceComplianceStatusReportFilter(
    string? Category = null,
    string? Severity = null,
    string? Department = null,
    Guid? ManagerId = null,
    DateOnly? DueDateStart = null,
    DateOnly? DueDateEnd = null,
    int Page = 1,
    int PageSize = 20);

public record GetGovernanceComplianceStatusReportResponse(
    List<GovernanceComplianceStatusRowModel> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool IsTruncated);

public record GovernanceComplianceStatusRowModel(
    Guid EmployeeId,
    string EmployeeName,
    string? Department,
    string Category,
    string CategoryLabel,
    string Detail,
    DateOnly? DueDate,
    string Severity);


public record GetReportFavouritesResponse(List<string> ReportIds);


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
