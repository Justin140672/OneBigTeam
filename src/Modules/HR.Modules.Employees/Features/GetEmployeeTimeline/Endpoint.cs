using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using IAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace HR.Modules.Employees.Features.GetEmployeeTimeline;

internal sealed class Endpoint(GetEmployeeTimelineHandler handler, IAuthorizationService authorizationService, ICurrentUser currentUser)
    : Endpoint<GetEmployeeTimelineRequest, GetEmployeeTimelineResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/timeline");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetEmployeeTimelineRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid callerId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var callerIsHr = (await authorizationService.AuthorizeAsync(User, "employee:manage")).Succeeded;

        var result = await handler.HandleAsync(request, callerId, callerIsHr, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
