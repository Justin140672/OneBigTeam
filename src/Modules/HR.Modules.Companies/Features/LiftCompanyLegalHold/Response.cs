namespace HR.Modules.Companies.Features.LiftCompanyLegalHold;

internal sealed record LiftCompanyLegalHoldResponse(Guid CompanyId, DateTimeOffset LegalHoldLiftedAt);
