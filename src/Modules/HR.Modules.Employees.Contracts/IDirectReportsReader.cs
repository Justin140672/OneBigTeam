namespace HR.Modules.Employees.Contracts;

public interface IDirectReportsReader
{
    Task<IReadOnlyList<Guid>> GetDirectReportIdsAsync(
        Guid companyId,
        Guid managerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns every employee in the reporting tree beneath <paramref name="managerId"/>,
    /// at any depth (direct reports, their reports, and so on) — not just direct reports.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAllDescendantIdsAsync(
        Guid companyId,
        Guid managerId,
        CancellationToken cancellationToken);
}
