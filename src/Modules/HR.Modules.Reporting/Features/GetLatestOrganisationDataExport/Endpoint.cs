using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetLatestOrganisationDataExport;

internal sealed class Endpoint(
    GetLatestOrganisationDataExportHandler handler,
    ICurrentUser currentUser)
    : Endpoint<GetLatestOrganisationDataExportRequest, GetLatestOrganisationDataExportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/data-exports/latest");
        Policies("role:company-administrator");
    }

    public override async Task HandleAsync(GetLatestOrganisationDataExportRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, userId, cancellationToken);
        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
