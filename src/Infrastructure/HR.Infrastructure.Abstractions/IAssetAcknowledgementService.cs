namespace HR.Infrastructure.Abstractions;

public interface IAssetAcknowledgementService
{
    Task AcknowledgeAsync(Guid companyId, Guid assignmentId, Guid acknowledgedBy, CancellationToken cancellationToken);
}
