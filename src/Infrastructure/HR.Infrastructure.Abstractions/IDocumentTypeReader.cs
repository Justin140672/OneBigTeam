namespace HR.Infrastructure.Abstractions;

public interface IDocumentTypeReader
{
    Task<bool> ExistsAsync(Guid companyId, Guid documentTypeId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        Guid companyId,
        IEnumerable<Guid> documentTypeIds,
        CancellationToken cancellationToken);
}
