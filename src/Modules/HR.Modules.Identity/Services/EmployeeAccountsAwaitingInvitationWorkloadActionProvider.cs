using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Services;

/// <summary>
/// OBT-721 Workload &amp; HR Actions Report provider for employee accounts awaiting invitation.
/// HR-only. Identity owns UserInvite directly, so this queries IdentityDbContext rather than going
/// through a cross-module reader — same derivation rules documented on
/// EmployeeUserAccountStatusReader (PendingInvitation = not claimed/cancelled/expired,
/// InvitationExpired = not claimed/cancelled but past ExpiresAt). Employees with no invite at all
/// ("NoUser") are out of scope here — there is nothing outstanding to action until HR sends an
/// invite in the first place.
/// </summary>
internal sealed class EmployeeAccountsAwaitingInvitationWorkloadActionProvider(
    IdentityDbContext dbContext,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService) : IWorkloadActionProvider
{
    public string ActionCategory => "Employee Accounts Awaiting Invitation";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;
        if (!callerIsHr)
            return [];

        var invites = await dbContext.UserInvites
            .AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.ClaimedAt == null && i.CancelledAt == null)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        if (invites.Count == 0)
            return [];

        // Only the most recent outstanding invite per employee — resending creates fresh
        // token/expiry on the same row (UserInvite.Resend) so there is at most one live row per
        // employee in practice, but keep this defensive in case of legacy duplicates.
        var latestPerEmployee = invites
            .GroupBy(i => i.EmployeeId)
            .Select(g => g.First())
            .ToList();

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(
            companyId, latestPerEmployee.Select(i => i.EmployeeId), cancellationToken);

        return latestPerEmployee.Select(invite =>
        {
            departments.TryGetValue(invite.EmployeeId, out var dept);
            var expired = invite.IsExpired;

            return new WorkloadAction(
                EmployeeId: invite.EmployeeId,
                EmployeeName: dept?.EmployeeName ?? invite.EmployeeId.ToString(),
                Department: dept?.DepartmentName,
                ActionType: expired ? "Resend Expired Invitation" : "Awaiting Invitation Acceptance",
                ActionCategory: ActionCategory,
                DueDate: DateOnly.FromDateTime(invite.ExpiresAt.UtcDateTime),
                AssignedTo: null,
                Status: expired ? "Invitation Expired" : "Pending Invitation",
                DeepLinkUrl: $"/companies/{companyId}/user-administration/{invite.EmployeeId}");
        }).ToList();
    }
}
