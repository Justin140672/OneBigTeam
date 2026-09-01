namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Cross-module read surface for the organisation data export job to obtain all Leave-module
/// data for a company (leave requests, allowances, policies, and public holidays where owned).
/// Implemented by an internal service in HR.Modules.Leave, DI-registered in LeaveModule.
/// Must enforce company_id.
/// </summary>
public interface ILeaveDataExportSource
{
    Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken);
}
