using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Notifications.Features.SendProductUpdate;

internal sealed class Endpoint(
    SendProductUpdateHandler handler) : Endpoint<SendProductUpdateRequest, SendProductUpdateResponse>
{
    public override void Configure()
    {
        Post("/api/notifications/admin/product-updates");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(SendProductUpdateRequest req, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(req, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
