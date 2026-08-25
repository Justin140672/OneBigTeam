using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Notifications.Features.MarkAllNotificationsRead;

internal sealed class Endpoint(MarkAllNotificationsReadHandler handler, ICurrentUser currentUser)
    : Endpoint<MarkAllNotificationsReadRequest>
{
    public override void Configure()
    {
        // The {employeeId} route segment is retained for URL-shape compatibility only. NOT-01:
        // it is never trusted — the recipient is always the authenticated caller, resolved
        // server-side via ICurrentUser. There is no HR/admin bypass here: notifications are
        // private per-employee data, unlike Documents/Leave/etc.
        Put("/api/companies/{companyId:guid}/employees/{employeeId:guid}/notifications/read-all");
        Policies("role:employee");
    }

    public override async Task HandleAsync(MarkAllNotificationsReadRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        await handler.HandleAsync(
            new MarkAllNotificationsReadRequest
            {
                CompanyId = request.CompanyId,
                EmployeeId = callerEmployeeId,
            },
            cancellationToken);
        await Send.NoContentAsync(cancellationToken);
    }
}
