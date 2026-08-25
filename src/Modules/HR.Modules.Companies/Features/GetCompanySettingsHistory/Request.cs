namespace HR.Modules.Companies.Features.GetCompanySettingsHistory;

internal sealed record GetCompanySettingsHistoryRequest
{
    public Guid CompanyId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
