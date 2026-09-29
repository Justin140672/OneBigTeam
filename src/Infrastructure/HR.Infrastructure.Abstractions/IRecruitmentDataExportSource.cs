namespace HR.Infrastructure.Abstractions;

public interface IRecruitmentDataExportSource
{
    Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken);
}
