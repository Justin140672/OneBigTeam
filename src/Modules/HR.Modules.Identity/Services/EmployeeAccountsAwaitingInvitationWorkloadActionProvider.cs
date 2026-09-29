using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Services;

internal sealed class EmployeeAccountsAwaitingInvitationWorkloadActionProvider(
    IdentityDbContext dbContext,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService) : IWorkloadActionProvider
{
    public string ActionCategory => "Employee Accounts Awaiting Invitation";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
        CancellationToken cancellationToken)
    {
        if (requestedScope != WorkloadScope.Hr)
            return [];

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
