namespace HR.Infrastructure.Abstractions;

public interface IOutstandingDocumentRequestReader
{
    Task<IReadOnlyList<OutstandingDocumentRequestItem>> GetOutstandingRequestsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken);
}
