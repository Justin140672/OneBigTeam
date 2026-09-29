using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Notifications.Features.GetMyNotifications;

internal sealed class Endpoint(GetMyNotificationsHandler handler, ICurrentUser currentUser)
    : Endpoint<GetMyNotificationsRequest, GetMyNotificationsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/notifications/my");
        Policies("role:employee");
    }

    public override async Task HandleAsync(GetMyNotificationsRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } employeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(
            new GetMyNotificationsRequest
            {
                CompanyId = request.CompanyId,
                EmployeeId = employeeId,
                IsRead = request.IsRead,
                Type = request.Type,
                Priority = request.Priority,
                CreatedFrom = request.CreatedFrom,
                CreatedTo = request.CreatedTo,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
            },
            cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
