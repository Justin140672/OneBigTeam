namespace HR.Infrastructure.Abstractions;

public interface IAuditDataExportSource
{
    Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken);
}
