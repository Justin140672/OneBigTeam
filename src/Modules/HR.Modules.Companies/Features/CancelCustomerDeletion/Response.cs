namespace HR.Modules.Companies.Features.CancelCustomerDeletion;

internal sealed record CancelCustomerDeletionResponse(Guid CompanyId, DateTimeOffset DeletionCancelledAt);
