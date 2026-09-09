using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Notifications.Features.ResolveOperationalAlert;

internal sealed class Endpoint(
    ResolveOperationalAlertHandler handler) : Endpoint<ResolveOperationalAlertRequest, ResolveOperationalAlertResponse>
{
    public override void Configure()
    {
        Post("/api/notifications/admin/operational-alerts/{alertId:guid}/resolve");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(ResolveOperationalAlertRequest req, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(req, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "unauthorized")
            {
                await Send.ResultAsync(TypedResults.Unauthorized());
                return;
            }

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound());
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
