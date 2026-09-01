namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Cross-module read surface for the organisation data export job to obtain all Sickness-module
/// data for a company (sickness records, evidence metadata, return-to-work reviews). Implemented
/// by an internal service in HR.Modules.Sickness, DI-registered in SicknessModule.
/// Must enforce company_id. Evidence file bytes are not included, only metadata.
/// </summary>
public interface ISicknessDataExportSource
{
    Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken);
}
