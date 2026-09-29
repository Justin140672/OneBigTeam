namespace HR.Infrastructure.Abstractions;

public interface ISharedCompanyDocumentAcknowledgementHistoryReplayer
{
    Task<int> ReplaySharedCompanyDocumentAcknowledgedAsync(Guid companyId, CancellationToken cancellationToken);
}
