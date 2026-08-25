using FastEndpoints;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.DeleteEmployeeDocument;

internal sealed class Endpoint(
    DeleteEmployeeDocumentHandler handler,
    ICurrentUser currentUser,
    DocumentResourceAuthorizer authorizer) : Endpoint<DeleteEmployeeDocumentRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/employees/{employeeId:guid}/documents/{employeeDocumentId:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        DeleteEmployeeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        // Reads the DB-resolved user id via ICurrentUser, not a raw ClaimTypes.NameIdentifier claim
        // — the JWT bearer handler is configured with MapInboundClaims = false (see HR.Api's
        // ConfigureSupabaseJwtBearer), so real Supabase-issued tokens never populate that mapped
        // claim type; relying on it directly would Unauthorized every request unconditionally.
        if (currentUser.UserId is not Guid deletedBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Reads the DB-resolved tenant via ICurrentUser, not a raw "company_id" JWT claim — real
        // Supabase-issued tokens never carry one, so relying on the claim directly would Forbid
        // every request unconditionally (see TenantRouteAuthorizationMiddleware). "employee:manage"
        // above only proves the HrAdministrator role, not company membership matching the route.
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        // DOC-01: routed through the same centralised authorizer as the read endpoints for
        // consistency, even though "employee:manage" already restricts this endpoint to HR
        // Administrators (who are always in-scope company-wide via DocumentResourceAuthorizer).
        if (!await authorizer.CanAccessEmployeeDocumentsAsync(
                request.CompanyId, deletedBy, request.EmployeeId, cancellationToken))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, deletedBy, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
