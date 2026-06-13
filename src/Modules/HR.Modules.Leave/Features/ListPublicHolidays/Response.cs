namespace HR.Modules.Leave.Features.ListPublicHolidays;

internal sealed record ListPublicHolidaysResponse(IReadOnlyList<PublicHolidayItem> Items);

internal sealed record PublicHolidayItem(
    Guid Id,
    Guid CompanyId,
    DateOnly Date,
    string Name,
    string CountryCode,
    DateTimeOffset CreatedAt);
