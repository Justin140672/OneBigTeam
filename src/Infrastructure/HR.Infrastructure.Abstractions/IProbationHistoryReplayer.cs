namespace HR.Infrastructure.Abstractions;

public interface IProbationHistoryReplayer
{
    Task<int> ReplayProbationPassedAsync(Guid companyId, CancellationToken cancellationToken);
}
