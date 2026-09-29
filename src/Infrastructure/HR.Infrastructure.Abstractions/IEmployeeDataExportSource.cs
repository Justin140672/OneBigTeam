namespace HR.Infrastructure.Abstractions;

public interface IEmployeeDataExportSource
{
    Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken);
}
