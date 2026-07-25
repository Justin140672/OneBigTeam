using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Notifications.Features.GetUnreadNotificationCount;

internal sealed class Endpoint(GetUnreadNotificationCountHandler handler)
    : Endpoint<GetUnreadNotificationCountRequest, GetUnreadNotificationCountResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/notifications/unread-count");
        Policies("role:employee");
    }

    public override async Task HandleAsync(GetUnreadNotificationCountRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var employeeId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(
            new GetUnreadNotificationCountRequest { CompanyId = request.CompanyId, EmployeeId = employeeId },
            cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
