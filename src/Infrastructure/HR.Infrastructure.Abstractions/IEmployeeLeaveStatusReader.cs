namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Lets other modules (e.g. Employees' My Team widget) ask "which of these employees are on
/// approved leave today?" without reaching into the Leave module's own schema.
/// </summary>
public interface IEmployeeLeaveStatusReader
{
    /// <summary>Returns the subset of employeeIds with an approved leave request covering today.</summary>
    Task<IReadOnlySet<Guid>> GetOnLeaveTodayEmployeeIdsAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken);
}
