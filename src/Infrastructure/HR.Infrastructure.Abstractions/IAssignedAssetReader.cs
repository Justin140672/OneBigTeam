namespace HR.Infrastructure.Abstractions;

public interface IAssignedAssetReader
{
    Task<IReadOnlyList<AssignedAssetItem>> GetAssignedAssetsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedAssetItem>>> GetAssignedAssetsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken cancellationToken);
}
