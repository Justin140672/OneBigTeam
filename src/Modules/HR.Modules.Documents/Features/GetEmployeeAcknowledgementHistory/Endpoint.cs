using System.Security.Claims;
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
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var callerId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Verify the caller belongs to the company in the route (applies to all callers). Reads
        // the DB-resolved tenant via ICurrentUser, not a raw "company_id" JWT claim — real
        // Supabase-issued tokens never carry one, so relying on the claim directly would Forbid
        // every request unconditionally (see TenantRouteAuthorizationMiddleware).
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        // Self-service: an employee viewing their own history is always allowed. Otherwise the
        // caller must hold the same policy used elsewhere in this module for HR managing shared
        // documents.
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
