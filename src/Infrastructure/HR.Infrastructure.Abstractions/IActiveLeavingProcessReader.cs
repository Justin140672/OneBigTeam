namespace HR.Infrastructure.Abstractions;

public interface IActiveLeavingProcessReader
{
    Task<IReadOnlyList<ActiveLeavingProcessItem>> GetInProgressLeavingProcessesAsync(
        CancellationToken cancellationToken);
}

public sealed record ActiveLeavingProcessItem(Guid CompanyId, Guid EmployeeId, DateOnly LastWorkingDay);
