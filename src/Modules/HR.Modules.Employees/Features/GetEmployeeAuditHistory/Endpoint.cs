using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using IAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace HR.Modules.Employees.Features.GetEmployeeAuditHistory;

internal sealed class Endpoint(
    GetEmployeeAuditHistoryHandler handler,
    IAuthorizationService authorizationService,
    ICurrentUser currentUser) : EndpointWithoutRequest<GetEmployeeAuditHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/audit-history");
        Policies("role:employee");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var companyId  = Route<Guid>("companyId");
        var employeeId = Route<Guid>("employeeId");

        if (currentUser.UserId is not Guid callerId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != companyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var callerIsHr = (await authorizationService.AuthorizeAsync(User, "employee:manage")).Succeeded;

        var result = await handler.HandleAsync(companyId, employeeId, callerId, callerIsHr, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
