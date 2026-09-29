namespace HR.Modules.Employees.Contracts;

public interface IDirectReportsReader
{
    Task<IReadOnlyList<Guid>> GetDirectReportIdsAsync(
        Guid companyId,
        Guid managerId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetAllDescendantIdsAsync(
        Guid companyId,
        Guid managerId,
        CancellationToken cancellationToken);
}
