namespace HR.Modules.Leave.Features.UpdatePublicHoliday;

internal sealed record UpdatePublicHolidayResponse(
    Guid Id,
    Guid CompanyId,
    DateOnly Date,
    string Name,
    string CountryCode,
    DateTimeOffset CreatedAt);
