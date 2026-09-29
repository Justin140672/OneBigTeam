namespace HR.Infrastructure.Abstractions;

public interface IEmployeeLeaveStatusReader
{
    Task<IReadOnlySet<Guid>> GetOnLeaveTodayEmployeeIdsAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken);
}
