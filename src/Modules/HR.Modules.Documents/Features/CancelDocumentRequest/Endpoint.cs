using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using IAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace HR.Modules.Documents.Features.CancelDocumentRequest;

internal sealed class Endpoint(CancelDocumentRequestHandler handler, IAuthorizationService authorizationService, ICurrentUser currentUser)
    : Endpoint<CancelDocumentRequestRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/employees/{employeeId:guid}/document-requests/{documentRequestId:guid}");
        Policies("role:employee");
    }

    public override async Task HandleAsync(CancelDocumentRequestRequest request, CancellationToken cancellationToken)
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

        if (!(await authorizationService.AuthorizeAsync(User, "employee:manage")).Succeeded)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            new CancelDocumentRequestRequest
            {
                CompanyId = request.CompanyId,
                EmployeeId = request.EmployeeId,
                DocumentRequestId = request.DocumentRequestId,
                IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
            },
            callerId,
            cancellationToken);

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

        await Send.ResultAsync(TypedResults.NoContent());
    }
}
