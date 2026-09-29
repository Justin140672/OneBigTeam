namespace HR.Infrastructure.Abstractions;

public interface IDocumentTypeDefaultsProvisioner
{
    Task EnsureDefaultDocumentTypesAsync(Guid companyId, CancellationToken cancellationToken);
}
