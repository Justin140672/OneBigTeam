namespace HR.Infrastructure.Abstractions;

public interface ILeaveImportWriter
{
    Task<bool> TryLayOpeningBalanceAsync(
        Guid companyId,
        Guid employeeId,
        string leaveTypeCode,
        decimal openingBalanceDays,
        Guid adjustedByEmployeeId,
        CancellationToken cancellationToken);
}
