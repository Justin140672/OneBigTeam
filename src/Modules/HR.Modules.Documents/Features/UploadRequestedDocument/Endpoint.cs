using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using IAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace HR.Modules.Documents.Features.UploadRequestedDocument;

internal sealed class Endpoint(UploadRequestedDocumentHandler handler, IAuthorizationService authorizationService, ICurrentUser currentUser)
    : Endpoint<UploadRequestedDocumentRequest, UploadRequestedDocumentResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/document-requests/{documentRequestId:guid}/upload");
        Policies("role:employee");
        AllowFileUploads();
    }

    public override async Task HandleAsync(
        UploadRequestedDocumentRequest request,
        CancellationToken cancellationToken)
    {
        // Reads the DB-resolved user id via ICurrentUser, not a raw ClaimTypes.NameIdentifier claim
        // — the JWT bearer handler is configured with MapInboundClaims = false (see HR.Api's
        // ConfigureSupabaseJwtBearer), so real Supabase-issued tokens never populate that mapped
        // claim type; relying on it directly would Unauthorized every request unconditionally.
        if (currentUser.UserId is not Guid uploadedBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Reads the DB-resolved tenant via ICurrentUser, not a raw "company_id" JWT claim — real
        // Supabase-issued tokens never carry one, so relying on the claim directly would Forbid
        // every request unconditionally (see TenantRouteAuthorizationMiddleware).
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var isManager = (await authorizationService.AuthorizeAsync(User, "employee:manage")).Succeeded;
        if (!isManager && uploadedBy != request.EmployeeId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, uploadedBy, cancellationToken);

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
            $"/api/companies/{v.CompanyId}/employees/{v.EmployeeId}/documents/{v.EmployeeDocumentId}",
            v));
    }
}
