namespace HR.Modules.Employees.Contracts;

public interface IPositionProfileDocumentsReader
{
    Task<IReadOnlyList<PositionProfileRequiredDocumentItem>> GetActiveDocumentsAsync(
        Guid companyId,
        Guid positionProfileId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the number of active PositionProfileRequiredDocument rows (across all position
    /// profiles in the company) that reference the given DocumentTypeId. Used by
    /// Documents.DeactivateDocumentType to block deactivation of a document type that is still
    /// required by a position profile, without a direct module-to-module reference or database join.
    /// </summary>
    Task<int> CountActiveReferencesToDocumentTypeAsync(
        Guid companyId,
        Guid documentTypeId,
        CancellationToken cancellationToken);
}
