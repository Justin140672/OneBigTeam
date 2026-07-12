namespace HR.Infrastructure.Abstractions;

public interface IProbationStatusReader
{
    Task<ProbationStatusSummary?> GetStatusAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken);
}
