using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Notifications.Features.ResolveAdministrativeAlert;

internal sealed class Endpoint(ResolveAdministrativeAlertHandler handler, ICurrentUser currentUser)
    : Endpoint<ResolveAdministrativeAlertRequest>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/administrative-alerts/{alertId:guid}/resolve");
        Policies("admin-alerts:view");
    }

    public override async Task HandleAsync(ResolveAdministrativeAlertRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var outcome = await handler.HandleAsync(
            new ResolveAdministrativeAlertRequest
            {
                CompanyId = request.CompanyId,
                AlertId = request.AlertId,
                ResolutionNote = request.ResolutionNote,
                ActorUserId = userId,
            },
            cancellationToken);

        switch (outcome)
        {
            case ResolveAdministrativeAlertOutcome.NotFound:
                await Send.ResultAsync(TypedResults.NotFound());
                return;
            case ResolveAdministrativeAlertOutcome.Conflict:
                await Send.ResultAsync(TypedResults.Conflict());
                return;
            default:
                await Send.ResultAsync(TypedResults.NoContent());
                return;
        }
    }
}
