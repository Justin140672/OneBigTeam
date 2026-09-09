using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Notifications.Features.ListOperationalAlerts;

internal sealed class Endpoint(
    ListOperationalAlertsHandler handler) : Endpoint<ListOperationalAlertsRequest, ListOperationalAlertsResponse>
{
    public override void Configure()
    {
        Get("/api/notifications/admin/operational-alerts");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(ListOperationalAlertsRequest req, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(req, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "unauthorized")
            {
                await Send.ResultAsync(TypedResults.Unauthorized());
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
