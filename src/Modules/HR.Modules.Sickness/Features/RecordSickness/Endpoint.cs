using FastEndpoints;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.RecordSickness;

internal sealed class Endpoint(
    RecordSicknessHandler handler,
    IManagerReader managerReader,
    ICurrentUser currentUser,
    IAuthorizationService authorizationService)
    : Endpoint<RecordSicknessRequest, RecordSicknessResponse>
{
    // Mirrors HR.Modules.Identity.Domain.SystemPermissions.SicknessManage. Sickness cannot reference
    // Identity's internal SystemPermissions/SystemRoles directly, so the permission id is duplicated
    // here as the sanctioned escape hatch for checking a policy other than the endpoint's own
    // (see GetTeamSicknessToday.Endpoint for the established pattern).
    private static readonly Guid SicknessManagePermissionId = new("00000000-0000-0000-0001-000000000015");

    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/sickness-records");
        // "sickness:view-team" (Manager + HrAdministrator). Managers are restricted below to
        // recording sickness for their own direct reports only; HR administrators may record for
        // any employee in the company.
        Policies("sickness:view-team");
    }

    public override async Task HandleAsync(RecordSicknessRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var canManageAnyEmployee = userId is not null
            && await authorizationService.HasPermissionAsync(userId.Value, SicknessManagePermissionId, cancellationToken);

        if (!canManageAnyEmployee)
        {
            var managerId = await managerReader.GetManagerIdAsync(request.CompanyId, request.EmployeeId, cancellationToken);

            if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var authenticatedEmployeeId)
                || managerId != authenticatedEmployeeId)
            {
                await Send.ResultAsync(TypedResults.Forbid());
                return;
            }
        }

        var result = await handler.HandleAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
                return;
            }
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }
        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{request.CompanyId}/employees/{request.EmployeeId}/sickness-records/{result.Value!.Id}",
            result.Value));
    }
}
