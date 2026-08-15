namespace HR.Modules.Companies.Features.ExecuteCustomerDeletion;

internal sealed record ExecuteCustomerDeletionResponse(Guid CompanyId, DateTimeOffset DeletionExecutedAt);
