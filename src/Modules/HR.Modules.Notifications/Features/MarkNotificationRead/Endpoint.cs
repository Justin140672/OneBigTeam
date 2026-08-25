using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Notifications.Features.MarkNotificationRead;

internal sealed class Endpoint(MarkNotificationReadHandler handler, ICurrentUser currentUser)
    : Endpoint<MarkNotificationReadRequest>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/notifications/{notificationId:guid}/read");
        Policies("role:employee");
    }

    public override async Task HandleAsync(MarkNotificationReadRequest request, CancellationToken cancellationToken)
    {
        // NOT-01: the recipient is always the authenticated caller, resolved server-side via
        // ICurrentUser — never trust a route or body-supplied employee id. There is no HR/admin
        // bypass here: notifications are private per-employee data, unlike Documents/Leave/etc.
        if (currentUser.UserId is not { } callerEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(
            new MarkNotificationReadRequest
            {
                CompanyId = request.CompanyId,
                NotificationId = request.NotificationId,
                EmployeeId = callerEmployeeId,
            },
            cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
