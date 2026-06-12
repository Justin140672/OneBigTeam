namespace HR.SharedKernel;

public sealed record LeaveApprovedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeaveRequestId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    Guid ReviewedByEmployeeId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
