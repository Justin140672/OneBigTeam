namespace HR.Infrastructure.Abstractions;

public interface ILeaveDataExportSource
{
    Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken);
}
