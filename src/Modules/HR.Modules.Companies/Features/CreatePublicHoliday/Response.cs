namespace HR.Modules.Companies.Features.CreatePublicHoliday;

internal sealed record CreatePublicHolidayResponse(
    Guid Id,
    Guid CompanyId,
    DateOnly Date,
    string Name,
    string CountryCode,
    DateTimeOffset CreatedAt);
