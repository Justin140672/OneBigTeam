namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Reads assets currently assigned (not yet returned) to an employee, regardless of
/// acknowledgement status. Used by modules that need to know what physical assets an
/// employee still holds (e.g. Offboarding, to generate asset-return tasks).
/// </summary>
public interface IAssignedAssetReader
{
    Task<IReadOnlyList<AssignedAssetItem>> GetAssignedAssetsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Batch equivalent of <see cref="GetAssignedAssetsAsync(Guid,Guid,CancellationToken)"/> for a
    /// set of employees in one query — avoids N+1 queries when a caller (e.g. the Offboarding
    /// Progress Report, OBT-720) needs currently-assigned-asset status for many employees at once.
    /// Employees with no assigned assets are simply absent from the returned dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedAssetItem>>> GetAssignedAssetsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken cancellationToken);
}
