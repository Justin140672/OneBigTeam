using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Services;

/// <summary>
/// Implements <see cref="ILeaveImportWriter"/> for the DataImport module's confirm step.
/// Layers a non-default opening balance on top of the baseline LeaveBalance row already seeded
/// by InitialiseEmployeeLeave's EmployeeCreatedHandler, mirroring AdjustLeaveBalanceHandler's
/// hours/days conversion and negative-balance guard but expressed directly in days (the import
/// file's LeaveBalanceDays column) and always tagged Reason = Import.
/// </summary>
internal sealed class LeaveImportWriter(
    LeaveDbContext dbContext,
    IClock clock,
    ICompanyLeaveSettingsReader leaveSettingsReader,
    IAuditEventPublisher auditPublisher) : ILeaveImportWriter
{
    public async Task<bool> TryLayOpeningBalanceAsync(
        Guid companyId,
        Guid employeeId,
        string leaveTypeCode,
        decimal openingBalanceDays,
        Guid adjustedByEmployeeId,
        CancellationToken cancellationToken)
    {
        var leaveType = await dbContext.LeaveTypes
            .SingleOrDefaultAsync(
                lt => lt.CompanyId == companyId && lt.Code == leaveTypeCode && lt.IsActive && lt.HasBalance,
                cancellationToken);

        if (leaveType is null)
            return false;

        var leaveSettings = await leaveSettingsReader.GetLeaveSettingsAsync(companyId, cancellationToken);
        var policyYear = LeaveYearCalculator.GetPolicyYear(clock.UtcNowOffset(), leaveSettings.LeaveYearStartMonth);

        var balance = await dbContext.LeaveBalances
            .SingleOrDefaultAsync(
                b => b.CompanyId == companyId
                  && b.EmployeeId == employeeId
                  && b.LeaveTypeId == leaveType.Id
                  && b.PolicyYear == policyYear,
                cancellationToken);

        if (balance is null)
            return false;

        var currentTotal = balance.EntitlementDays + balance.AdjustmentDays;
        var adjustmentDays = openingBalanceDays - currentTotal;

        if (adjustmentDays == 0)
            return true;

        var now = clock.UtcNowOffset();

        balance.Adjust(adjustmentDays, now);

        var adjustment = LeaveBalanceAdjustment.Create(
            Guid.NewGuid(),
            companyId,
            employeeId,
            leaveType.Id,
            adjustmentDays,
            LeaveBalanceAdjustmentReason.Import,
            "Opening balance set during employee import.",
            adjustedByEmployeeId,
            now);

        dbContext.LeaveBalanceAdjustments.Add(adjustment);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new LeaveBalanceAdjustedAuditEvent(
            balance.CompanyId,
            balance.EmployeeId,
            balance.LeaveTypeId,
            balance.Id,
            balance.PolicyYear,
            adjustmentDays,
            balance.RemainingDays,
            adjustedByEmployeeId,
            now,
            AdjustmentHours: null,
            Reason: LeaveBalanceAdjustmentReason.Import.ToString()), cancellationToken);

        return true;
    }
}
