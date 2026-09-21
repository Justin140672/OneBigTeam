using FastEndpoints;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Notifications.Features.PreviewProductUpdateRecipients;

internal sealed class Endpoint(
    PreviewProductUpdateRecipientsHandler handler) : EndpointWithoutRequest<PreviewProductUpdateRecipientsResponse>
{
    public override void Configure()
    {
        Get("/api/notifications/admin/product-updates/recipient-preview");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
