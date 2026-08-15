namespace HR.Modules.Companies.Features.ForceCustomerReadOnly;

internal sealed record ForceCustomerReadOnlyResponse(Guid CompanyId, bool AdminForcedReadOnly);
