using FastEndpoints;

namespace HR.Modules.Notifications.Features.MarkAllNotificationsRead;

internal sealed class Endpoint(MarkAllNotificationsReadHandler handler)
    : Endpoint<MarkAllNotificationsReadRequest>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/employees/{employeeId:guid}/notifications/read-all");
        Policies("authenticated");
    }

    public override async Task HandleAsync(MarkAllNotificationsReadRequest request, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(request, cancellationToken);
        await SendNoContentAsync(cancellationToken);
    }
}
