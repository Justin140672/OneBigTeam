namespace HR.Infrastructure.Abstractions;

/// <summary>
/// DSH-05: date-aware "who is off sick on a given date" reader, owned by HR.Modules.Sickness.
///
/// Distinct from <see cref="IEmployeeSicknessStatusReader"/>, which answers "who has an active
/// sickness record right now" purely from record status. This variant also honours the record's
/// start date so a sickness record captured ahead of time does not count until the absence has
/// actually begun — required for correct date-boundary behaviour in the manager team-status
/// summary. An active (open) sickness record has no end date, so only the start bound is checked.
/// </summary>
public interface IEmployeesOffSickReader
{
    /// <summary>
    /// Returns the subset of <paramref name="employeeIds"/> with an active sickness record whose
    /// absence covers <paramref name="onDate"/>.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetOffSickEmployeeIdsAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        DateOnly onDate,
        CancellationToken cancellationToken);
}
