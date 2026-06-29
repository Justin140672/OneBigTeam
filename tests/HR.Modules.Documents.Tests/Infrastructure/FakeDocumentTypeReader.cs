using HR.SharedKernel.Contracts;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeDocumentTypeReader(IReadOnlyDictionary<Guid, string> names) : IDocumentTypeReader
{
    public Task<bool> ExistsAsync(Guid companyId, Guid documentTypeId, CancellationToken cancellationToken)
        => Task.FromResult(names.ContainsKey(documentTypeId));

    public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        Guid companyId,
        IEnumerable<Guid> documentTypeIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, string> result = documentTypeIds
            .Where(names.ContainsKey)
            .ToDictionary(id => id, id => names[id]);
        return Task.FromResult(result);
    }
}
