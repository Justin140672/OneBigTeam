using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.DataImport.Features.ListImportSessions;

// Note: mirrors UploadImportFile/ValidateImportSession's auth pattern exactly — see those
// endpoints for the rationale behind reusing "employee:manage" until a dedicated
// data-import permission exists.
internal sealed class Endpoint(ListImportSessionsHandler handler, IAuthorizationService authorizationService)
    : Endpoint<ListImportSessionsRequest, List<ImportSessionSummary>>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/data-import/sessions");
        Policies("role:employee");
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

        // Verify the caller belongs to the company in the route.
        var companyClaim = User.FindFirstValue("company_id");
        if (!Guid.TryParse(companyClaim, out var callerCompanyId) || callerCompanyId != request.CompanyId)
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
