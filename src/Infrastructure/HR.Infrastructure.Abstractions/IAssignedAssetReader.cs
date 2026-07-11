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
}
