using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.RequestAdditionalEmployeeDocument;

internal sealed class Endpoint(RequestAdditionalEmployeeDocumentHandler handler, IAuthorizationService authorizationService)
    : Endpoint<RequestAdditionalEmployeeDocumentRequest, RequestAdditionalEmployeeDocumentResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/document-requests");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        RequestAdditionalEmployeeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var callerId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var companyClaim = User.FindFirstValue("company_id");
        if (!Guid.TryParse(companyClaim, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        if (!(await authorizationService.AuthorizeAsync(User, "employee:manage")).Succeeded)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, callerId, cancellationToken);

        if (result.IsFailure)
        {
            var error = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(error));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(error));
                return;
            }

            await Send.ResultAsync(TypedResults.UnprocessableEntity(error));
            return;
        }

        var v = result.Value!;
        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{v.CompanyId}/employees/{v.EmployeeId}/document-requests/{v.DocumentRequestId}",
            v));
    }
}
