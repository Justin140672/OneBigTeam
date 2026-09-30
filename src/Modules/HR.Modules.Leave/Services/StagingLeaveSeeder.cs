using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Leave.Services;

internal static class StagingLeaveSeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        IEnumerable<(Guid EmployeeId, DateOnly StartDate)> employees)
    {
        var db = services.GetRequiredService<LeaveDbContext>();
        var leaveSettingsReader = services.GetRequiredService<ICompanyLeaveSettingsReader>();
        var companyId = StagingSeedOptions.CompanyId;
        var now = DateTimeOffset.UtcNow;

        var policyId = await db.LeavePolicies
            .Where(p => p.CompanyId == companyId && p.IsDefault && p.IsActive)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync();
        if (policyId is null)
        {
            return;
        }

        var leaveTypes = await db.LeaveTypes
            .Where(lt => lt.CompanyId == companyId && lt.IsActive && lt.HasBalance)
            .ToListAsync();
        if (leaveTypes.Count == 0)
        {
            return;
        }

        var leaveSettings = await leaveSettingsReader.GetLeaveSettingsAsync(companyId, CancellationToken.None);
        var policyYear = LeaveYearCalculator.GetPolicyYear(now, leaveSettings.LeaveYearStartMonth);
        var (policyYearStart, policyYearEnd) =
            LeaveYearCalculator.GetPolicyYearBounds(policyYear, leaveSettings.LeaveYearStartMonth);

        var assignedEmployeeIds = (await db.EmployeeLeavePolicyAssignments
                .Where(a => a.CompanyId == companyId)
                .Select(a => a.EmployeeId)
                .ToListAsync())
            .ToHashSet();

        var balanceKeys = (await db.LeaveBalances
                .Where(b => b.CompanyId == companyId && b.PolicyYear == policyYear)
                .Select(b => new { b.EmployeeId, b.LeaveTypeId })
                .ToListAsync())
            .Select(b => (b.EmployeeId, b.LeaveTypeId))
            .ToHashSet();

        foreach (var (employeeId, startDate) in employees)
        {
            if (assignedEmployeeIds.Add(employeeId))
            {
                db.EmployeeLeavePolicyAssignments.Add(EmployeeLeavePolicyAssignment.Create(
                    Guid.NewGuid(), companyId, employeeId, policyId.Value, startDate, now));
            }

            var accrualStartDate = startDate < policyYearStart ? policyYearStart : startDate;

            foreach (var leaveType in leaveTypes.Where(lt => !balanceKeys.Contains((employeeId, lt.Id))))
            {
                db.LeaveBalances.Add(LeaveBalance.Create(
                    Guid.NewGuid(), companyId, employeeId, leaveType.Id, policyId.Value, policyYear,
                    leaveType.Behaviour == LeaveTypeBehaviour.Toil
                        ? 0
                        : LeaveEntitlementCalculator.CalculateEntitlement(
                            leaveType.DefaultEntitlementDays, policyYearStart, policyYearEnd, startDate),
                    accrualStartDate, now));
            }
        }

        await db.SaveChangesAsync();
    }
}
