namespace HR.Infrastructure.Abstractions;

public interface ISicknessDataExportSource
{
    Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken);
}
