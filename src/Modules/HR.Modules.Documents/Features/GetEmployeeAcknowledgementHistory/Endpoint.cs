using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using IAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace HR.Modules.Documents.Features.GetEmployeeAcknowledgementHistory;

internal sealed class Endpoint(GetEmployeeAcknowledgementHistoryHandler handler, IAuthorizationService authorizationService, ICurrentUser currentUser)
    : Endpoint<GetEmployeeAcknowledgementHistoryRequest, GetEmployeeAcknowledgementHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/acknowledgement-history");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetEmployeeAcknowledgementHistoryRequest request,
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

        var isSelf = callerId == request.EmployeeId;
        if (!isSelf)
        {
            var isManager = (await authorizationService.AuthorizeAsync(User, "shared-document:manage")).Succeeded;
            if (!isManager)
            {
                await Send.ResultAsync(TypedResults.Forbid());
                return;
            }
        }

        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
