using FastEndpoints;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.ListEmployeeDocuments;

internal sealed class Endpoint(
    ListEmployeeDocumentsHandler handler,
    ICurrentUser currentUser,
    DocumentResourceAuthorizer authorizer) : Endpoint<ListEmployeeDocumentsRequest, ListEmployeeDocumentsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/documents");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        ListEmployeeDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        // Reads the DB-resolved user id via ICurrentUser, not a raw ClaimTypes.NameIdentifier claim
        // — the JWT bearer handler is configured with MapInboundClaims = false (see HR.Api's
        // ConfigureSupabaseJwtBearer), so real Supabase-issued tokens never populate that mapped
        // claim type; relying on it directly would Unauthorized every request unconditionally.
        if (currentUser.UserId is not Guid callerId)
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

        // DOC-01: self, any manager in the employee's complete reporting hierarchy, or an HR
        // Administrator may list this employee's documents. "role:employee" above only proves
        // the caller holds a role — it never proves a relationship to this specific employeeId.
        if (!await authorizer.CanAccessEmployeeDocumentsAsync(
                request.CompanyId, callerId, request.EmployeeId, cancellationToken))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
