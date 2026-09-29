using FastEndpoints;
using HR.Modules.Sickness.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.GetMissingFitNotes;

internal sealed class Endpoint(
    GetMissingFitNotesHandler handler,
    ICurrentUser currentUser,
    SicknessResourceAuthorizer authorizer) : Endpoint<GetMissingFitNotesRequest, GetMissingFitNotesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/sickness-evidence-requests/missing");
        Policies("sickness:review");
    }

    public override async Task HandleAsync(
        GetMissingFitNotesRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var authorizedEmployeeIds = await authorizer.GetAuthorizedEmployeeIdsAsync(
            request.CompanyId, callerId, cancellationToken);

        var response = await handler.HandleAsync(request, authorizedEmployeeIds, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
