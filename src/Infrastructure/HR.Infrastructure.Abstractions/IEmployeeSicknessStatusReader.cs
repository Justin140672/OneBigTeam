namespace HR.Infrastructure.Abstractions;

public interface IEmployeeSicknessStatusReader
{
    Task<IReadOnlySet<Guid>> GetSickEmployeeIdsAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken);
}
