namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Per-vacancy recruitment pipeline summary data for the Recruitment Pipeline Summary Report, as
/// owned by HR.Modules.Recruitment. Unlike <see cref="IRecruitmentPipelineReader"/> (which reports
/// funnel totals grouped by recruiter/vacancy), this reader returns one row per vacancy together with
/// the company's configured <see cref="RecruitmentStage"/> pipeline columns and, per vacancy, the
/// count of applications currently sitting at each stage.
/// </summary>
public interface IRecruitmentPipelineSummaryReader
{
    Task<RecruitmentPipelineSummaryResult> GetSummaryAsync(
        Guid companyId,
        bool includeClosed,
        CancellationToken cancellationToken);
}

public sealed record RecruitmentPipelineSummaryResult(
    IReadOnlyList<RecruitmentPipelineSummaryRow> Vacancies,
    IReadOnlyList<RecruitmentStageColumn> Stages);

public sealed record RecruitmentStageColumn(Guid StageId, string StageName);

public sealed record RecruitmentPipelineSummaryRow(
    Guid VacancyId,
    string VacancyTitle,
    string? PositionProfileTitle,
    string? DepartmentName,
    string Status,
    DateOnly? OpenedAt,
    int CandidateCount,
    // Count of applications currently sitting at each configured RecruitmentStage, keyed by StageId.
    // A stage with no applications currently on it for this vacancy is simply absent from the
    // dictionary — callers should treat a missing key as zero.
    IReadOnlyDictionary<Guid, int> CandidatesByStage);
