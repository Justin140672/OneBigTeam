using FastEndpoints;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.DownloadEmployeeDocument;

internal sealed class Endpoint(
    DownloadEmployeeDocumentHandler handler,
    ICurrentUser currentUser,
    DocumentResourceAuthorizer authorizer) : Endpoint<DownloadEmployeeDocumentRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/documents/{employeeDocumentId:guid}/download");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        DownloadEmployeeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        // Reads the DB-resolved user id via ICurrentUser, not a raw ClaimTypes.NameIdentifier claim
        // — the JWT bearer handler is configured with MapInboundClaims = false (see HR.Api's
        // ConfigureSupabaseJwtBearer), so real Supabase-issued tokens never populate that mapped
        // claim type; relying on it directly would Unauthorized every request unconditionally.
        if (currentUser.UserId is not Guid downloadedBy)
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

        // DOC-01: download exposes actual file content, so it must be gated by the same
        // self/manager-hierarchy/HR-administrator resource check as list/detail — not just the
        // "role:employee" role policy above, which every employee/manager/HR admin satisfies
        // regardless of any relationship to this specific employeeId.
        if (!await authorizer.CanAccessEmployeeDocumentsAsync(
                request.CompanyId, downloadedBy, request.EmployeeId, cancellationToken))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, downloadedBy, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.RedirectAsync(result.Value!.ToString(), isPermanent: false, allowRemoteRedirects: true);
    }
}
