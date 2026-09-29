namespace HR.Infrastructure.Abstractions;

public interface IOffboardingHistoryReplayer
{
    Task<int> ReplayStartedOffboardingsAsync(Guid companyId, CancellationToken cancellationToken);
}
