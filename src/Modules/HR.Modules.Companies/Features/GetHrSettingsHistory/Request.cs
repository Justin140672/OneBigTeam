namespace HR.Modules.Companies.Features.GetHrSettingsHistory;

internal sealed record GetHrSettingsHistoryRequest
{
    public Guid CompanyId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
