namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Cross-module write surface used exclusively by the DataImport confirm step to lay a
/// non-default opening leave balance on top of the baseline balance seeded by
/// InitialiseEmployeeLeave's EmployeeCreatedHandler. Implemented in HR.Modules.Leave.Services
/// and DI-registered in LeaveModule.
/// </summary>
public interface ILeaveImportWriter
{
    /// <summary>
    /// Adjusts the employee's current-policy-year balance for the leave type identified by
    /// <paramref name="leaveTypeCode"/> so its resulting entitlement matches
    /// <paramref name="openingBalanceDays"/>. Returns false (no-op) if the leave type code does
    /// not exist for the company, is not balance-tracked, or the baseline balance row has not
    /// yet been created for the employee.
    /// </summary>
    Task<bool> TryLayOpeningBalanceAsync(
        Guid companyId,
        Guid employeeId,
        string leaveTypeCode,
        decimal openingBalanceDays,
        Guid adjustedByEmployeeId,
        CancellationToken cancellationToken);
}
