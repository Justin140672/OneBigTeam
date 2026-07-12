namespace HR.Infrastructure.Abstractions;

public interface IOffboardingStatusReader
{
    Task<OffboardingStatusSummary?> GetStatusAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken);
}
