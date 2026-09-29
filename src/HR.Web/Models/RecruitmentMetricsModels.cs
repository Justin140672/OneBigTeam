namespace HR.Web.Models;


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

public sealed record RecruitmentMetricDrillDownRow(string Name, string VacancyTitle, DateTime Date);
