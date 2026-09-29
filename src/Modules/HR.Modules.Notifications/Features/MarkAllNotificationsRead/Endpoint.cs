using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Notifications.Features.MarkAllNotificationsRead;

internal sealed class Endpoint(MarkAllNotificationsReadHandler handler, ICurrentUser currentUser)
    : Endpoint<MarkAllNotificationsReadRequest>
{
    public override void Configure()
    {
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
