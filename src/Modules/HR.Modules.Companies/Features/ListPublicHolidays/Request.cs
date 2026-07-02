namespace HR.Modules.Companies.Features.ListPublicHolidays;

internal sealed record ListPublicHolidaysRequest
{
    public Guid CompanyId { get; init; }
}
