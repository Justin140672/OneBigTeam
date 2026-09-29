namespace HR.Infrastructure.Abstractions;

public interface IEmployeesOffSickReader
{
    Task<IReadOnlySet<Guid>> GetOffSickEmployeeIdsAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        DateOnly onDate,
        CancellationToken cancellationToken);
}
