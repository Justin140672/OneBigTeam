namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Read surface for the organisation data export job to obtain audit-log entries for a company.
/// Implemented in HR.Infrastructure itself because audit persistence is a cross-cutting
/// infrastructure capability (see DbAuditEventPublisher / audit-log query). Sensitive values are
/// already redacted by the existing audit scrubber before rows are returned. Must enforce company_id.
/// </summary>
public interface IAuditDataExportSource
{
    Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken);
}
