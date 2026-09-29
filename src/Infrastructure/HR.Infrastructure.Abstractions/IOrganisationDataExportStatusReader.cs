namespace HR.Infrastructure.Abstractions;

public interface IOrganisationDataExportStatusReader
{
    Task<bool> HasActiveExportAsync(Guid companyId, CancellationToken cancellationToken);
}
