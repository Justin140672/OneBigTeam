namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Lets other modules (e.g. Employees' My Team widget) ask "which of these employees are
/// currently off sick?" without reaching into the Sickness module's own schema.
/// </summary>
public interface IEmployeeSicknessStatusReader
{
    /// <summary>Returns the subset of employeeIds who have an active sickness record right now.</summary>
    Task<IReadOnlySet<Guid>> GetSickEmployeeIdsAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken);
}
