using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.DataImport.Features.GetImportSession;

// Note: mirrors UploadImportFile/ValidateImportSession's auth pattern exactly — see those
// endpoints for the rationale behind reusing "employee:manage" until a dedicated
// data-import permission exists.
internal sealed class Endpoint(GetImportSessionHandler handler, IAuthorizationService authorizationService)
    : Endpoint<GetImportSessionRequest, GetImportSessionResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/data-import/sessions/{importSessionId:guid}");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetImportSessionRequest request,
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

        if (result.IsFailure)
        {
            var error = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(error));
                return;
            }

            await Send.ResultAsync(TypedResults.UnprocessableEntity(error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
