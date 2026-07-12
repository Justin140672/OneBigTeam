namespace HR.Infrastructure.Abstractions;

public interface IOnboardingStatusReader
{
    Task<OnboardingStatusSummary?> GetStatusAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken);
}
