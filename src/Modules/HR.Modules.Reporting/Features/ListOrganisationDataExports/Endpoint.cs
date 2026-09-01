using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ListOrganisationDataExports;

internal sealed class Endpoint(
    ListOrganisationDataExportsHandler handler,
    ICurrentUser currentUser)
    : Endpoint<ListOrganisationDataExportsRequest, ListOrganisationDataExportsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/data-exports");
        Policies("role:company-administrator");
    }

    public override async Task HandleAsync(ListOrganisationDataExportsRequest request, CancellationToken cancellationToken)
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

        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
