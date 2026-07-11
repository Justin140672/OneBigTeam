using HR.Infrastructure.Abstractions;

namespace HR.Modules.Offboarding.Tests.Infrastructure;

internal sealed class FakeOutstandingDocumentRequestReader(
    IReadOnlyList<OutstandingDocumentRequestItem>? items = null) : IOutstandingDocumentRequestReader
{
    private readonly IReadOnlyList<OutstandingDocumentRequestItem> _items = items ?? [];

    public Task<IReadOnlyList<OutstandingDocumentRequestItem>> GetOutstandingRequestsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_items);
}
