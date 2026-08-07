using System.Security.Claims;
using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using IAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace HR.Modules.DataImport.Features.ListImportSessions;

// Note: mirrors UploadImportFile/ValidateImportSession's auth pattern exactly — see those
// endpoints for the rationale behind reusing "employee:manage" until a dedicated
// data-import permission exists.
internal sealed class Endpoint(ListImportSessionsHandler handler, IAuthorizationService authorizationService, ICurrentUser currentUser)
    : Endpoint<ListImportSessionsRequest, List<ImportSessionSummary>>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/data-import/sessions");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        ListImportSessionsRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out _))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Verify the caller belongs to the company in the route. Reads the DB-resolved tenant via
        // ICurrentUser, not a raw "company_id" JWT claim — real Supabase-issued tokens never carry
        // one, so relying on the claim directly would Forbid every request unconditionally (see
        // TenantRouteAuthorizationMiddleware).
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var isAuthorized = (await authorizationService.AuthorizeAsync(User, "employee:manage")).Succeeded;
        if (!isAuthorized)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
