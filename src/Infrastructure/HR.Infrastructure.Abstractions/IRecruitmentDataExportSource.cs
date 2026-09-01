namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Cross-module read surface for the organisation data export job to obtain all Recruitment-module
/// data for a company (vacancies, candidates, applications, interviews, offers). Implemented by an
/// internal service in HR.Modules.Recruitment, DI-registered in RecruitmentModule.
/// Must enforce company_id.
/// </summary>
public interface IRecruitmentDataExportSource
{
    Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken);
}
