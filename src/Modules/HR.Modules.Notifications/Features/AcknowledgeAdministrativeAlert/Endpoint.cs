using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Notifications.Features.AcknowledgeAdministrativeAlert;

internal sealed class Endpoint(AcknowledgeAdministrativeAlertHandler handler, ICurrentUser currentUser)
    : Endpoint<AcknowledgeAdministrativeAlertRequest>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/administrative-alerts/{alertId:guid}/acknowledge");
        Policies("admin-alerts:view");
    }

    public override async Task HandleAsync(AcknowledgeAdministrativeAlertRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var outcome = await handler.HandleAsync(
            new AcknowledgeAdministrativeAlertRequest
            {
                CompanyId = request.CompanyId,
                AlertId = request.AlertId,
                ActorUserId = userId,
            },
            cancellationToken);

        switch (outcome)
        {
            case AcknowledgeAdministrativeAlertOutcome.NotFound:
                await Send.ResultAsync(TypedResults.NotFound());
                return;
            case AcknowledgeAdministrativeAlertOutcome.Conflict:
                await Send.ResultAsync(TypedResults.Conflict());
                return;
            default:
                await Send.ResultAsync(TypedResults.NoContent());
                return;
        }
    }
}
