using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Notifications.Features.GetUnreadNotificationCount;

internal sealed class Endpoint(GetUnreadNotificationCountHandler handler)
    : Endpoint<GetUnreadNotificationCountRequest, GetUnreadNotificationCountResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/notifications/unread-count");
        Policies("authenticated");
    }

    public override async Task HandleAsync(GetUnreadNotificationCountRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var employeeId))
        {
            await SendResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(
            new GetUnreadNotificationCountRequest { CompanyId = request.CompanyId, EmployeeId = employeeId },
            cancellationToken);

        await SendAsync(result, StatusCodes.Status200OK, cancellationToken);
    }
}
