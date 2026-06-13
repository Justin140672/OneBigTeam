namespace HR.Modules.Leave.Features.CreatePublicHoliday;

internal sealed record CreatePublicHolidayRequest
{
    public Guid CompanyId { get; init; }
    public DateOnly Date { get; init; }
    public string Name { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
}
