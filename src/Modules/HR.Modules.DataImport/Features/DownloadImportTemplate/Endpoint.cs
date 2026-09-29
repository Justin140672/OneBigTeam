using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using IAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace HR.Modules.DataImport.Features.DownloadImportTemplate;

// Note: mirrors ExportImportErrors/ValidateImportSession's auth pattern exactly — see those
// endpoints for the rationale behind reusing "employee:manage" until a dedicated
// data-import permission exists.
internal sealed class Endpoint(DownloadImportTemplateHandler handler, IAuthorizationService authorizationService, ICurrentUser currentUser)
    : Endpoint<DownloadImportTemplateRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/data-import/employees/template");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        DownloadImportTemplateRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

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

        var bytes = handler.Handle();

        await Send.ResultAsync(TypedResults.File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "employee-import-template.xlsx"));
    }
}
