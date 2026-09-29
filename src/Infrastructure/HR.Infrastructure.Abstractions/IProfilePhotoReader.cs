using HR.Modules.Employees.Contracts;
namespace HR.Infrastructure.Abstractions;

public interface IProfilePhotoReader
{
    Task<IReadOnlyDictionary<Guid, string>> GetCurrentPhotoUrlsAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken);
}
