namespace HR.Infrastructure.Abstractions;

public interface IOnboardingHistoryReplayer
{
    Task<int> ReplayOnboardingCompletedAsync(Guid companyId, CancellationToken cancellationToken);
}
