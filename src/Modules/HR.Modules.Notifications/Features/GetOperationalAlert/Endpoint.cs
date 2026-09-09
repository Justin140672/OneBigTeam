using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Notifications.Features.GetOperationalAlert;

internal sealed class Endpoint(
    GetOperationalAlertHandler handler) : Endpoint<GetOperationalAlertRequest, GetOperationalAlertResponse>
{
    public override void Configure()
    {
        Get("/api/notifications/admin/operational-alerts/{alertId:guid}");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(GetOperationalAlertRequest req, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(req, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound());
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
