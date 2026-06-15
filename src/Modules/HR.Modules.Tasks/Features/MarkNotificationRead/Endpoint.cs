using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.MarkNotificationRead;

internal sealed class Endpoint(MarkNotificationReadHandler handler)
    : Endpoint<MarkNotificationReadRequest>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/notifications/{notificationId:guid}/read");
        Policies("authenticated");
    }

    public override async Task HandleAsync(MarkNotificationReadRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await SendResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await SendNoContentAsync(cancellationToken);
    }
}
