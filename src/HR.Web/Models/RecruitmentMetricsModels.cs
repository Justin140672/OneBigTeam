namespace HR.Web.Models;

// DSH-04: client-side mirrors of the authoritative recruitment dashboard metric endpoints
// (specifications/product-specifications/recruitment-dashboard-metrics.md). Every endpoint returns
// { count, items[] } where count == items.Count by construction, so a tile and its drill-down list
// can never disagree. JSON string enums via HrApiJsonOptions.Default, matching the codebase convention.

// Uniform application row shared by the new-applications, candidates-in-progress and
// offers-awaiting-response metrics.
public sealed record RecruitmentMetricApplicationItem(
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    string CandidateEmail,
    Guid VacancyId,
    string VacancyTitle,
    Guid StageId,
    string StageName,
    DateTimeOffset AppliedAt);

public sealed record RecruitmentMetricInterviewItem(
    Guid InterviewId,
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    Guid VacancyId,
    string VacancyTitle,
    DateTimeOffset ScheduledAt,
    string? Location);

public sealed record NewApplicationsMetricResponse(
    int Count,
    bool DefinedByStagePurpose,
    IReadOnlyList<RecruitmentMetricApplicationItem> Items);

public sealed record CandidatesInProgressMetricResponse(
    int Count,
    IReadOnlyList<RecruitmentMetricApplicationItem> Items);

public sealed record OffersAwaitingResponseMetricResponse(
    int Count,
    bool OfferStageConfigured,
    IReadOnlyList<RecruitmentMetricApplicationItem> Items);

public sealed record InterviewsRequiringActionMetricResponse(
    int Count,
    IReadOnlyList<RecruitmentMetricInterviewItem> Items);

// Flattened row for the dashboard drill-down dialog (RecruitmentMetricDrillDownDialog).
public sealed record RecruitmentMetricDrillDownRow(string Name, string VacancyTitle, DateTime Date);
