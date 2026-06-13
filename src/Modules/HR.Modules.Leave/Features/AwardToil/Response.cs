namespace HR.Modules.Leave.Features.AwardToil;

internal sealed record AwardToilResponse(
    Guid TransactionId,
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeaveBalanceId,
    Guid AwardedByEmployeeId,
    decimal Days,
    decimal BalanceRemainingDays,
    DateOnly OccurredOn,
    string? Notes,
    DateTimeOffset AwardedAt);
