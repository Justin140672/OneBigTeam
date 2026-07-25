namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Provides the set of "current" (non-former) employee IDs for a company, as owned by
/// HR.Modules.Employees. Used by other modules that need to filter their own local records down
/// to only those belonging to current employees (e.g. Leave's DeactivateLeaveType guard, which must
/// not count LeaveBalance rows belonging to former employees), without a direct module-to-module
/// reference or database join.
/// </summary>
public interface ICurrentEmployeeReader
{
    /// <summary>
    /// Returns the IDs of every employee in the given company whose status is not FormerEmployee.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetCurrentEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken);
}
