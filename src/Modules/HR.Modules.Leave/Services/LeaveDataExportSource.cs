using System.Globalization;
using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Services;

/// <summary>
/// Story 2: contributes the Leave module's principal data (leave requests, balances/allowances,
/// policies) to the organisation data export. company_id enforced on every query.
/// </summary>
internal sealed class LeaveDataExportSource(LeaveDbContext db) : ILeaveDataExportSource
{
    public async Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var requests = await db.LeaveRequests.AsNoTracking()
            .Where(r => r.CompanyId == companyId)
            .Select(r => new { r.Id, r.EmployeeId, r.LeaveTypeId, r.LeavePolicyId, r.Status, r.StartDate, r.EndDate, r.TotalDays, r.Reason, r.ReviewedByEmployeeId, r.ReviewedAt, r.RejectionReason, r.CreatedAt })
            .ToListAsync(cancellationToken);

        var requestsTable = new DataExportTable(
            "leave_requests",
            ["Id", "EmployeeId", "LeaveTypeId", "LeavePolicyId", "Status", "StartDate", "EndDate", "TotalDays", "Reason", "ReviewedByEmployeeId", "ReviewedAt", "RejectionReason", "CreatedAt"],
            requests.Select(r => (IReadOnlyList<string?>)new string?[]
            {
                r.Id.ToString(), r.EmployeeId.ToString(), r.LeaveTypeId.ToString(), r.LeavePolicyId?.ToString(),
                r.Status.ToString(), D(r.StartDate), D(r.EndDate), N(r.TotalDays), r.Reason,
                r.ReviewedByEmployeeId?.ToString(), T(r.ReviewedAt), r.RejectionReason, T(r.CreatedAt)
            }).ToList());

        var balances = await db.LeaveBalances.AsNoTracking()
            .Where(b => b.CompanyId == companyId)
            .Select(b => new { b.Id, b.EmployeeId, b.LeaveTypeId, b.LeavePolicyId, b.PolicyYear, b.EntitlementDays, b.UsedDays, b.AdjustmentDays })
            .ToListAsync(cancellationToken);

        var balancesTable = new DataExportTable(
            "leave_allowances",
            ["Id", "EmployeeId", "LeaveTypeId", "LeavePolicyId", "PolicyYear", "EntitlementDays", "UsedDays", "AdjustmentDays"],
            balances.Select(b => (IReadOnlyList<string?>)new string?[]
            {
                b.Id.ToString(), b.EmployeeId.ToString(), b.LeaveTypeId.ToString(), b.LeavePolicyId.ToString(),
                b.PolicyYear.ToString(CultureInfo.InvariantCulture), N(b.EntitlementDays), N(b.UsedDays), N(b.AdjustmentDays)
            }).ToList());

        var policies = await db.LeavePolicies.AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .Select(p => new { p.Id, p.Name, p.Description, p.CarryOverDays, p.RequiresApproval, p.IsActive, p.IsDefault })
            .ToListAsync(cancellationToken);

        var policiesTable = new DataExportTable(
            "leave_policies",
            ["Id", "Name", "Description", "CarryOverDays", "RequiresApproval", "IsActive", "IsDefault"],
            policies.Select(p => (IReadOnlyList<string?>)new string?[]
            {
                p.Id.ToString(), p.Name, p.Description, p.CarryOverDays.ToString(CultureInfo.InvariantCulture),
                B(p.RequiresApproval), B(p.IsActive), B(p.IsDefault)
            }).ToList());

        return [requestsTable, balancesTable, policiesTable];
    }

    private static string D(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string? T(DateTimeOffset? value) => value?.ToString("o", CultureInfo.InvariantCulture);
    private static string T(DateTimeOffset value) => value.ToString("o", CultureInfo.InvariantCulture);
    private static string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static string B(bool value) => value ? "true" : "false";
}
