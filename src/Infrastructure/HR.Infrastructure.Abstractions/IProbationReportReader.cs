namespace HR.Infrastructure.Abstractions;

public interface IProbationReportReader
{
    Task<IReadOnlyList<ProbationReportItem>> GetProbationReportAsync(
        Guid companyId,
        IReadOnlyCollection<Guid>? employeeIds,
        CancellationToken cancellationToken);
}

public sealed record ProbationReportItem(
    Guid EmployeeId,
    Guid RecordId,
    string Status,
    DateOnly StartDate,
    DateOnly ExpectedEndDate,
    int DueReviewCount,
    int OverdueReviewCount);
