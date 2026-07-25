using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.DataImport.Features.DownloadImportTemplate;

// Note: mirrors ExportImportErrors/ValidateImportSession's auth pattern exactly — see those
// endpoints for the rationale behind reusing "employee:manage" until a dedicated
// data-import permission exists.
internal sealed class Endpoint(DownloadImportTemplateHandler handler, IAuthorizationService authorizationService)
    : Endpoint<DownloadImportTemplateRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/data-import/employees/template");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        DownloadImportTemplateRequest request,
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

        var bytes = handler.Handle();

        await Send.ResultAsync(TypedResults.File(
            bytes,
            "text/csv",
            "employee-import-template.csv"));
    }
}
