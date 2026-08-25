using FastEndpoints;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.RestoreEmployeeDocument;

// DOC-04: "authorised HR users can ... restore archived documents" — HR-only, same narrower
// scope as GetArchivedEmployeeDocuments (IsHrAdministratorAsync), distinct from the
// self/manager-hierarchy scope used for normal document access.
internal sealed class Endpoint(
    RestoreEmployeeDocumentHandler handler,
    ICurrentUser currentUser,
    DocumentResourceAuthorizer authorizer) : Endpoint<RestoreEmployeeDocumentRequest, RestoreEmployeeDocumentResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/documents/{employeeDocumentId:guid}/restore");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        RestoreEmployeeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid restoredBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        if (!await authorizer.IsHrAdministratorAsync(restoredBy, cancellationToken))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, restoredBy, cancellationToken);

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

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
