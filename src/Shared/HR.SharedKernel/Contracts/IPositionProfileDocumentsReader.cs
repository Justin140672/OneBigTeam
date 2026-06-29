namespace HR.SharedKernel.Contracts;

public interface IPositionProfileDocumentsReader
{
    Task<IReadOnlyList<PositionProfileRequiredDocumentItem>> GetActiveDocumentsAsync(
        Guid companyId,
        Guid positionProfileId,
        CancellationToken cancellationToken);
}
