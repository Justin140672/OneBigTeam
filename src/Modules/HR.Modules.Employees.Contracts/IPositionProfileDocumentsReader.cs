namespace HR.Modules.Employees.Contracts;

public interface IPositionProfileDocumentsReader
{
    Task<IReadOnlyList<PositionProfileRequiredDocumentItem>> GetActiveDocumentsAsync(
        Guid companyId,
        Guid positionProfileId,
        CancellationToken cancellationToken);

    Task<int> CountActiveReferencesToDocumentTypeAsync(
        Guid companyId,
        Guid documentTypeId,
        CancellationToken cancellationToken);
}
