namespace HR.Infrastructure.Abstractions;

public interface IPositionProfileDocumentsReader
{
    Task<IReadOnlyList<PositionProfileRequiredDocumentItem>> GetActiveDocumentsAsync(
        Guid companyId,
        Guid positionProfileId,
        CancellationToken cancellationToken);
}
