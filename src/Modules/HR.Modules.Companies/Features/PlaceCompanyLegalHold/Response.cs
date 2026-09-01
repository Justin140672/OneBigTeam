namespace HR.Modules.Companies.Features.PlaceCompanyLegalHold;

internal sealed record PlaceCompanyLegalHoldResponse(Guid CompanyId, DateTimeOffset LegalHoldPlacedAt);
