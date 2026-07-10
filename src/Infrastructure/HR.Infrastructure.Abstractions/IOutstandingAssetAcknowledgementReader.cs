namespace HR.Infrastructure.Abstractions;

public interface IOutstandingAssetAcknowledgementReader
{
    Task<IReadOnlyList<OutstandingAssetAcknowledgementItem>> GetOutstandingAcknowledgementsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken);
}
