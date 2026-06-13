namespace HR.Modules.Leave.Features.UpdatePublicHoliday;

internal sealed record UpdatePublicHolidayRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public DateOnly Date { get; init; }
    public string Name { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
}
