namespace HR.SharedKernel;

public sealed record LeaveRejectedIntegrationEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeaveRequestId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    Guid ReviewedByEmployeeId,
    string? RejectionReason,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
