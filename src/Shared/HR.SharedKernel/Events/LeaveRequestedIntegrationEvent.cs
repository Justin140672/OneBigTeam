namespace HR.SharedKernel;

public sealed record LeaveRequestedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeaveRequestId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
