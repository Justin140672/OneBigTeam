namespace HR.Modules.Companies.Features.ScheduleCustomerDeletion;

internal sealed record ScheduleCustomerDeletionResponse(Guid CompanyId, DateTimeOffset DeletionScheduledAt);
