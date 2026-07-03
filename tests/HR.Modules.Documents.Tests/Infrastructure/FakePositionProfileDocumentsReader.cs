using HR.Infrastructure.Abstractions;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakePositionProfileDocumentsReader(
    IReadOnlyList<PositionProfileRequiredDocumentItem> items) : IPositionProfileDocumentsReader
{
    public Task<IReadOnlyList<PositionProfileRequiredDocumentItem>> GetActiveDocumentsAsync(
        Guid companyId,
        Guid positionProfileId,
        CancellationToken cancellationToken)
        => Task.FromResult(items);
}
