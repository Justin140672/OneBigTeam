namespace HR.Infrastructure.Abstractions;

public interface IProbationSummaryReader
{
    Task<ProbationSummaryItem?> GetSummaryAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken);
}
