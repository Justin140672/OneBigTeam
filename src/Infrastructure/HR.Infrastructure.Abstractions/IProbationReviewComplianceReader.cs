namespace HR.Infrastructure.Abstractions;

/// <summary>
/// ADM-02 Compliance Centre: company-wide pending probation reviews, owned by HR.Modules.Probation.
/// Distinct from <see cref="IProbationReportReader"/> (which returns per-record counts) — this
/// returns one row per pending review with its due date so the Compliance Centre can present each
/// review as an individual actionable item and classify it overdue / due-soon centrally.
/// </summary>
public interface IProbationReviewComplianceReader
{
    Task<IReadOnlyList<ProbationReviewComplianceItem>> GetPendingProbationReviewsAsync(
        Guid companyId,
        CancellationToken cancellationToken);
}

public sealed record ProbationReviewComplianceItem(
    Guid EmployeeId,
    Guid ProbationReviewId,
    string ReviewType,
    DateOnly DueDate);
