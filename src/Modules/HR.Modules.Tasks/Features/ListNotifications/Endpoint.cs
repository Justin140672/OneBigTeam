using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.ListNotifications;

internal sealed class Endpoint(ListNotificationsHandler handler)
    : Endpoint<ListNotificationsRequest, ListNotificationsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/notifications");
        Policies("authenticated");
    }

    public override async Task HandleAsync(ListNotificationsRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await SendAsync(result, StatusCodes.Status200OK, cancellationToken);
    }
}
