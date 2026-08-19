using HR.Modules.Employees.Contracts;
namespace HR.Infrastructure.Abstractions;

public interface IProfilePhotoReader
{
    /// <summary>
    /// Returns a map of employeeId -> current (approved/live) profile photo download URL for the
    /// supplied employee IDs within a company. Employees without a live profile photo are simply
    /// absent from the returned dictionary — same "not found = absent" convention as
    /// <see cref="IEmployeeNameReader"/>.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetCurrentPhotoUrlsAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken);
}
