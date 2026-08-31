namespace HR.Infrastructure.Abstractions;

/// <summary>
/// DSH-05: lets the manager team-status summary (owned by HR.Modules.Employees) ask "which of
/// these employees have an outstanding fit-note / sickness-evidence request?" without reaching
/// into the Sickness module's schema.
///
/// Uses the same predicate as the Sickness module's own <c>GetMissingFitNotes</c> feature — an
/// evidence request in Pending or Overdue status — so the summary count and that feature's
/// drill-down list agree for the same population of employees.
/// </summary>
public interface IEmployeesMissingFitNoteReader
{
    /// <summary>
    /// Returns the subset of <paramref name="employeeIds"/> with at least one Pending or Overdue
    /// sickness-evidence request.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetEmployeeIdsMissingFitNotesAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken);
}
