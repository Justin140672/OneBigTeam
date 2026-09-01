using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.RequestOrganisationDataExport;

/// <summary>
/// Story 2: a company administrator requests a full downloadable export of their organisation's
/// data ahead of account closure. Gated by "role:company-administrator" and a caller-tenant check,
/// mirroring DOC-04's PurgeEligibleArchivedEmployeeDocuments endpoint.
/// </summary>
internal sealed class Endpoint(
    RequestOrganisationDataExportHandler handler,
    ICurrentUser currentUser)
    : Endpoint<RequestOrganisationDataExportRequest, RequestOrganisationDataExportResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/reporting/data-exports");
        Policies("role:company-administrator");
    }

    public override async Task HandleAsync(RequestOrganisationDataExportRequest request, CancellationToken cancellationToken)
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

        var result = await handler.HandleAsync(request, userId, currentUser.Email, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };
            await Send.ResultAsync(result.Error.Code == "conflict"
                ? TypedResults.Conflict(businessError)
                : TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
